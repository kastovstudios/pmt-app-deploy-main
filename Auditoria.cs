using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Globalization;

public static class Auditoria
{
    public record Admin(int Id, string Nome);
    public static void Inicializar(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"CREATE TABLE IF NOT EXISTS AtividadesAdmin (
            Id INTEGER PRIMARY KEY AUTOINCREMENT, AdminId INTEGER NOT NULL, AdminNome TEXT NOT NULL,
            DataHora TEXT NOT NULL, Acao TEXT NOT NULL, Detalhes TEXT NOT NULL, Resultado TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS IX_AtividadesAdmin_Data ON AtividadesAdmin(DataHora, Id);";
        cmd.ExecuteNonQuery();
    }
    public static Admin? Identificar(SqliteConnection conn, string token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, TRIM(COALESCE(Posto, '') || ' ' || Nome) FROM Usuarios WHERE Token = @t AND IsAdmin = 1";
        cmd.Parameters.AddWithValue("@t", token);
        using var r = cmd.ExecuteReader(); return r.Read() ? new Admin(r.GetInt32(0), r.GetString(1)) : null;
    }
    public static void Registrar(Admin admin, string acao, string detalhes, string resultado)
    {
        using var conn = Database.GetConnection(); conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO AtividadesAdmin(AdminId,AdminNome,DataHora,Acao,Detalhes,Resultado) VALUES(@id,@nome,@data,@acao,@detalhes,@resultado)";
        cmd.Parameters.AddWithValue("@id", admin.Id); cmd.Parameters.AddWithValue("@nome", admin.Nome);
        cmd.Parameters.AddWithValue("@data", TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time")).ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@acao", acao); cmd.Parameters.AddWithValue("@detalhes", detalhes);
        cmd.Parameters.AddWithValue("@resultado", resultado); cmd.ExecuteNonQuery();
    }

    // Lista explícita: nunca armazena Authorization, telefone, senha, token nem o corpo bruto.
    public static string Descrever(string method, string route) => (method, route) switch
    {
        ("POST", "/login") => "Login de administrador",
        ("POST", "/logout") => "Saiu do aplicativo",
        ("POST", "/alterar-senha-obrigatoria") => "Alterou a própria senha",
        ("POST", "/comprar") => "Realizou uma compra",
        ("GET", "/setup/admin/{telefone}") => "Concedeu acesso de administrador",
        ("POST", "/admin/produto") => "Cadastrou produto",
        ("PUT", "/admin/produto/{id}") => "Editou produto",
        ("PUT", "/admin/produto/{id}/preco") => "Alterou preço do produto",
        ("DELETE", "/admin/produto/{id}") => "Excluiu produto",
        ("POST", "/admin/estoque/adicionar") => "Adicionou estoque",
        ("POST", "/admin/estoque/{produtoId}/zerar") => "Zerou estoque do produto (saldo final: 0)",
        ("POST", "/admin/adicionar") => "Lançou compra para cliente",
        ("DELETE", "/admin/remover/{id}") => "Removeu item de compra (devolução ao estoque)",
        ("DELETE", "/admin/zerar/{userId}/{periodoId}") => "Zerou dívida (remoção dos consumos e devolução ao estoque)",
        ("PUT", "/admin/pagamento/{usuarioId}/{periodoId}") => "Atualizou pagamento mensal",
        ("PUT", "/admin/compras-diarias/{tipo}/{id:long}/pagamento") => "Atualizou pagamento de compra",
        ("DELETE", "/admin/compras-diarias/{tipo}/{id:long}") => "Removeu compra (devolução ao estoque)",
        ("POST", "/admin/usuario/{id}/resetar-senha") => "Resetou senha de cliente",
        ("PUT", "/admin/usuario/{id}") => "Editou cadastro de cliente",
        ("DELETE", "/admin/usuario/{id}") => "Excluiu cliente",
        ("POST", "/admin/fechar-mes") => "Fechou mês e abriu novo período",
        ("POST", "/admin/cobrar-cliente") => "Solicitou cobrança individual",
        ("POST", "/admin/cobrar-clientes/{periodoId}") => "Executou cobrança do mês",
        ("GET", "/admin/mensagens-cobranca/{periodoId}") => "Baixou mensagens de cobrança",
        ("POST", "/admin/atividades/whatsapp") => "Abriu cobrança no WhatsApp (envio não confirmado)",
        ("GET", "/admin/atividades") => "Consultou atividades dos administradores",
        ("GET", "/admin/clientes") => "Consultou lista de clientes",
        ("GET", "/admin/clientes-saldos/{periodoId}") => "Consultou saldos dos clientes",
        ("GET", "/admin/historico/{id}/{periodoId}") => "Consultou histórico de cliente",
        ("GET", "/admin/dashboard/{periodoId}") => "Consultou resumo do mês",
        ("GET", "/admin/clientes-devendo/{periodoId}") => "Consultou clientes com dívida",
        ("GET", "/admin/periodos") => "Consultou períodos",
        ("GET", "/admin/estoque/{produtoId}") => "Consultou estoque",
        ("GET", "/admin/relatorio/produto/{produtoId}/{periodoId}") => "Consultou relatório de produto",
        ("GET", "/admin/compras-diarias") => "Consultou compras diárias",
        _ when route.StartsWith("/admin/") && method == "GET" => "Consultou " + route.Replace("/admin/", ""),
        _ => ""
    };

    public static async Task<string> Detalhar(HttpRequest request, string route, SqliteConnection conn)
    {
        var detalhes = new List<string>(); var ids = new Dictionary<string, long>();
        foreach (var key in new[] { "id", "userId", "usuarioId", "produtoId", "periodoId" })
            if (long.TryParse(request.RouteValues[key]?.ToString(), out var id)) ids[key] = id;
        if (request.Query.TryGetValue("dia", out var dia) && DateOnly.TryParseExact(dia, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            detalhes.Add("Dia: " + date.ToString("dd/MM/yyyy"));
        if (request.HasJsonContentType() && route != "/login" && route != "/alterar-senha-obrigatoria")
        {
            request.EnableBuffering();
            try
            {
                using var doc = await JsonDocument.ParseAsync(request.Body);
                void LerObjeto(JsonElement item)
                {
                    if (item.ValueKind != JsonValueKind.Object) return;
                    bool Campo(string key, out JsonElement value)
                    {
                        foreach (var prop in item.EnumerateObject())
                            if (prop.Name.Equals(key, StringComparison.OrdinalIgnoreCase)) { value = prop.Value; return true; }
                        value = default; return false;
                    }
                    foreach (var field in new[] { "usuarioId", "produtoId", "periodoId" })
                        if (Campo(field, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)) ids[field] = n;
                    foreach (var field in new[] { "quantidade", "preco", "totalEsperado" })
                        if (Campo(field, out var v) && v.ValueKind == JsonValueKind.Number) detalhes.Add(field + ": " + v.GetRawText());
                    if (Campo("pago", out var pago) && pago.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        detalhes.Add(pago.GetBoolean() ? "Marcou como pago" : "Desfez marcação de pago");
                    if (Campo("nome", out var nome) && nome.ValueKind == JsonValueKind.String)
                        detalhes.Add("Nome informado: " + (nome.GetString() ?? "")[..Math.Min(nome.GetString()!.Length, 160)]);
                    if (route == "/comprar" && ids.TryGetValue("produtoId", out var prod)) detalhes.Add("Produto #" + prod);
                }
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    foreach (var item in doc.RootElement.EnumerateArray()) LerObjeto(item);
                else LerObjeto(doc.RootElement);
            }
            catch (JsonException) { detalhes.Add("Dados inválidos na requisição"); }
            finally { request.Body.Position = 0; }
        }
        string Nome(string sql, long id)
        {
            using var cmd = conn.CreateCommand(); cmd.CommandText = sql; cmd.Parameters.AddWithValue("@id", id);
            return Convert.ToString(cmd.ExecuteScalar()) ?? "não encontrado";
        }
        foreach (var (key, value) in ids) detalhes.Add(key + ": " + value);
        var user = ids.GetValueOrDefault("usuarioId", ids.GetValueOrDefault("userId"));
        if (route.Contains("/usuario/") || route.StartsWith("/admin/historico/")) user = ids.GetValueOrDefault("id");
        if (route == "/setup/admin/{telefone}")
        {
            using var alvo = conn.CreateCommand();
            alvo.CommandText = "SELECT Id FROM Usuarios WHERE Telefone=@telefone";
            alvo.Parameters.AddWithValue("@telefone", request.RouteValues["telefone"]?.ToString() ?? "");
            user = Convert.ToInt64(alvo.ExecuteScalar());
            detalhes.Add("Cliente #" + user);
        }
        if (user > 0) detalhes.Add("Cliente: " + Nome("SELECT TRIM(COALESCE(Posto,'') || ' ' || Nome) FROM Usuarios WHERE Id=@id", user));
        var product = ids.GetValueOrDefault("produtoId");
        if (route.StartsWith("/admin/produto/")) product = ids.GetValueOrDefault("id");
        if (product > 0) detalhes.Add("Produto antes: " + Nome("SELECT Nome || ' | preço R$ ' || Preco || ' | estoque ' || COALESCE((SELECT Quantidade FROM Estoque WHERE ProdutoId=Produtos.Id),0) FROM Produtos WHERE Id=@id", product));
        if (ids.TryGetValue("periodoId", out var period)) detalhes.Add("Mês: " + Nome("SELECT Nome FROM Periodos WHERE Id=@id", period));
        if (route.Contains("/compras-diarias/") || route == "/admin/remover/{id}")
        {
            var tipo = request.RouteValues["tipo"]?.ToString();
            var filtro = tipo == "compra" ? "c.CompraId=@id" : "c.Id=@id";
            var itens = ComprasDiarias.Ler(conn, null, filtro, ("@id", ids.GetValueOrDefault("id")));
            foreach (var item in itens) detalhes.Add($"{item.Posto} {item.Nome}: {item.Quantidade}x {item.Produto}, R$ {item.Total:F2}, mês #{item.PeriodoId}");
        }
        if (route == "/admin/fechar-mes")
            detalhes.Add("Período aberto antes: " + Nome("SELECT Nome FROM Periodos WHERE Fechado=0 ORDER BY Id DESC LIMIT 1", 0));
        return string.Join("; ", detalhes);
    }

    public static void Mapear(WebApplication app)
    {
        app.MapGet("/admin/atividades", (HttpRequest request) =>
        {
            using var conn = Database.GetConnection(); conn.Open();
            if (Identificar(conn, request.Headers.Authorization.ToString()) == null) return Results.Unauthorized();
            if (!DateOnly.TryParseExact(request.Query["dia"], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dia) || dia.Year == 9999)
                return Results.BadRequest("Selecione uma data válida.");
            long antes = long.MaxValue;
            if (request.Query.ContainsKey("antes") && (!long.TryParse(request.Query["antes"], out antes) || antes <= 0))
                return Results.BadRequest("Página inválida.");
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id,AdminNome,DataHora,Acao,Detalhes,Resultado FROM AtividadesAdmin WHERE DataHora >= @inicio AND DataHora < @fim AND Id < @antes ORDER BY Id DESC LIMIT 101";
            cmd.Parameters.AddWithValue("@inicio", dia.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@fim", dia.AddDays(1).ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@antes", antes);
            using var reader = cmd.ExecuteReader();
            var itens = new List<object>(); long ultimo = 0; bool mais = false;
            while (reader.Read())
            {
                if (itens.Count == 100) { mais = true; break; }
                ultimo = reader.GetInt64(0);
                itens.Add(new { id = ultimo, admin = reader.GetString(1), dataHora = reader.GetString(2), acao = reader.GetString(3), detalhes = reader.GetString(4), resultado = reader.GetString(5) });
            }
            return Results.Ok(new { itens, proximaPagina = mais ? (long?)ultimo : null });
        });
        app.MapPost("/logout", (HttpRequest request) =>
        {
            using var conn = Database.GetConnection(); conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Usuarios SET Token=NULL WHERE Token=@token AND Token IS NOT NULL AND Token<>''";
            cmd.Parameters.AddWithValue("@token", request.Headers.Authorization.ToString());
            cmd.ExecuteNonQuery(); return Results.Ok();
        });
        app.MapPost("/admin/atividades/whatsapp", (CobrarClienteDTO dados, HttpRequest request) =>
        {
            using var conn = Database.GetConnection(); conn.Open();
            if (Identificar(conn, request.Headers.Authorization.ToString()) == null) return Results.Unauthorized();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM Usuarios WHERE Id=@u) AND EXISTS(SELECT 1 FROM Periodos WHERE Id=@p)";
            cmd.Parameters.AddWithValue("@u", dados.UsuarioId); cmd.Parameters.AddWithValue("@p", dados.PeriodoId);
            return Convert.ToInt32(cmd.ExecuteScalar()) == 1 ? Results.Ok() : Results.BadRequest("Cliente ou mês inválido.");
        });
    }

    public static void Instalar(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var route = (context.GetEndpoint() as Microsoft.AspNetCore.Routing.RouteEndpoint)?.RoutePattern.RawText ?? "";
            var acao = Descrever(context.Request.Method, route);
            if (acao == "") { await next(); return; }
            Admin? admin; string detalhes;
            using (var conn = Database.GetConnection())
            {
                conn.Open(); admin = Identificar(conn, context.Request.Headers.Authorization.ToString());
                detalhes = admin != null ? await Detalhar(context.Request, route, conn) : "";
            }
            bool registrado = false;
            void Gravar(string resultado)
            {
                var autor = context.Items["AdminLogin"] as Admin ?? admin;
                if (registrado || autor == null) return;
                registrado = true;
                try { Registrar(autor, acao, detalhes, resultado); }
                catch (Exception ex) { app.Logger.LogError(ex, "Falha ao persistir auditoria de {Acao} por admin {AdminId}", acao, autor.Id); }
            }
            context.Response.OnStarting(() => { Gravar(context.Response.StatusCode is >= 200 and < 300 ? "Concluída" : $"Não concluída (HTTP {context.Response.StatusCode})"); return Task.CompletedTask; });
            try { await next(); }
            catch { Gravar("Falha no processamento; verificar possível alteração parcial"); throw; }
        });
    }
}
