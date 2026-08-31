using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;
using PelComSystem.DTOs;
using DotNetEnv;

var builder = WebApplication.CreateBuilder(args);
Env.Load();
string evolutionUrl =
    Environment.GetEnvironmentVariable("EVOLUTION_URL");

string evolutionInstance =
    Environment.GetEnvironmentVariable("EVOLUTION_INSTANCE");

string evolutionApiKey =
    Environment.GetEnvironmentVariable("EVOLUTION_API_KEY");

builder.WebHost.UseUrls("http://0.0.0.0:8080");

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors();

Database.Inicializar();

void GarantirPeriodoAtual()
{
    using var conn = Database.GetConnection();
    conn.Open();

    var existeAberto = conn.CreateCommand();
    existeAberto.CommandText = @"
        SELECT COUNT(*)
        FROM Periodos
        WHERE Fechado = 0
    ";

    int abertos = Convert.ToInt32(existeAberto.ExecuteScalar());

    if (abertos > 0)
        return;

    TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");

    var agora = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);

    string nome = agora.ToString(
        "MMMM/yyyy",
        new System.Globalization.CultureInfo("pt-BR")
    );

    var cmd = conn.CreateCommand();

    cmd.CommandText = @"
        INSERT INTO Periodos
        (Nome, Mes, Ano, Fechado)
        VALUES
        (@nome, @mes, @ano, 0)
    ";

    cmd.Parameters.AddWithValue("@nome", nome);
    cmd.Parameters.AddWithValue("@mes", agora.Month);
    cmd.Parameters.AddWithValue("@ano", agora.Year);

    cmd.ExecuteNonQuery();
}

GarantirPeriodoAtual();
// =======================
//  HELPERS
// =======================


string HashSenha(string senha)
{
    var sha = SHA256.Create();
    return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(senha)));
}

int? GetUserId(HttpRequest request)
{
    if (!request.Headers.TryGetValue("Authorization", out var token))
        return null;

    using var conn = Database.GetConnection();
    conn.Open();

    var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT Id FROM Usuarios WHERE Token = @t";
    cmd.Parameters.AddWithValue("@t", token.ToString());

    var reader = cmd.ExecuteReader();
    if (!reader.Read()) return null;

    return reader.GetInt32(0);
}

bool IsAdmin(HttpRequest request)
{
    if (!request.Headers.TryGetValue("Authorization", out var token))
        return false;

    using var conn = Database.GetConnection();
    conn.Open();

    var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT IsAdmin FROM Usuarios WHERE Token = @t";
    cmd.Parameters.AddWithValue("@t", token.ToString());

    var reader = cmd.ExecuteReader();
    return reader.Read() && reader.GetInt32(0) == 1;
}

int GetPeriodoAbertoId(SqliteConnection conn)
{
    var cmd = conn.CreateCommand();

    cmd.CommandText = @"
        SELECT Id
        FROM Periodos
        WHERE Fechado = 0
        ORDER BY Ano DESC, Mes DESC
        LIMIT 1";

    var result = cmd.ExecuteScalar();

    if (result == null)
        throw new Exception("Nenhum período aberto encontrado.");

    return Convert.ToInt32(result);
}

// =======================
//  AUTH
// =======================

app.MapPost("/register", (RegisterDTO dto) =>
{

    if(string.IsNullOrWhiteSpace(dto.Nome))
        return Results.BadRequest("Nome obrigatório.");

    if(string.IsNullOrWhiteSpace(dto.Telefone))
        return Results.BadRequest("Telefone obrigatório.");

    if(string.IsNullOrWhiteSpace(dto.Senha))
        return Results.BadRequest("Senha obrigatória.");

    if(string.IsNullOrWhiteSpace(dto.Posto))
        return Results.BadRequest("Posto obrigatório.");

    using var conn = Database.GetConnection();
    conn.Open();

    var cmd = conn.CreateCommand();

    cmd.CommandText = @"
        INSERT INTO Usuarios
        (Nome, Telefone, Posto, SenhaHash)

        VALUES
        (@n, @t, @p, @s)
    ";
    string telefone =
    new string(dto.Telefone
        .Where(char.IsDigit)
        .ToArray());
        
    cmd.Parameters.AddWithValue("@n", dto.Nome);

    cmd.Parameters.AddWithValue("@t", telefone);

    cmd.Parameters.AddWithValue("@p", dto.Posto);

    cmd.Parameters.AddWithValue("@s",
        HashSenha(dto.Senha));

    try
    {
        cmd.ExecuteNonQuery();

        return Results.Ok();
    }
    catch
    {
        return Results.BadRequest(
            "Telefone já existe"
        );
    }
});

app.MapPost("/login", (LoginDTO dto) =>
{
    using var conn = Database.GetConnection();
    conn.Open();

    var cmd = conn.CreateCommand();

    cmd.CommandText = @"
        SELECT *
        FROM Usuarios
        WHERE Telefone = @t
    ";

    string telefone =
    new string(dto.Telefone
        .Where(char.IsDigit)
        .ToArray());

    cmd.Parameters.AddWithValue("@t",
        telefone);

    var reader = cmd.ExecuteReader();

    if (!reader.Read())
        return Results.Unauthorized();

    if (reader["SenhaHash"].ToString()!= HashSenha(dto.Senha))
    {
        return Results.Unauthorized();
    }

    int id =
        Convert.ToInt32(reader["Id"]);

    reader.Close();

    string token =
        Guid.NewGuid().ToString();

    var update = conn.CreateCommand();

    update.CommandText = @"
        UPDATE Usuarios
        SET Token = @t
        WHERE Id = @id
    ";

    update.Parameters.AddWithValue("@t",
        token);

    update.Parameters.AddWithValue("@id",
        id);

    update.ExecuteNonQuery();

    return Results.Ok(new
    {
        token
    });
});

app.MapGet("/me", (HttpRequest request) =>
{
    string token = request.Headers["Authorization"];

    if (string.IsNullOrEmpty(token))
        return Results.Unauthorized();

    using var conn = Database.GetConnection();
    conn.Open();

    var cmd = conn.CreateCommand();

    cmd.CommandText = @"
        SELECT
            Nome,
            Posto,
            Telefone,
            IsAdmin
        FROM Usuarios
        WHERE Token = @t
    ";

    cmd.Parameters.AddWithValue("@t", token);

    var reader = cmd.ExecuteReader();

    if (!reader.Read())
        return Results.Unauthorized();

    return Results.Ok(new
    {
        nome = reader["Nome"],
        posto = reader["Posto"],
        telefone = reader["Telefone"],
        admin = Convert.ToInt32(reader["IsAdmin"]) == 1
    });
});


// =======================
//  PRODUTOS
// =======================

app.MapGet("/produtos", () =>
{
    using var conn = Database.GetConnection();
    conn.Open();

    var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT * FROM Produtos";

    var reader = cmd.ExecuteReader();
    var lista = new List<object>();

    while (reader.Read())
    {
        lista.Add(new
        {
            id = reader.GetInt32(0),
            nome = reader.GetString(1),
            preco = reader.GetDouble(2)
        });
    }

    return Results.Ok(lista);
});

app.MapPost("/admin/produto", async (HttpRequest request) =>
{
    if (!IsAdmin(request)) return Results.Unauthorized();

    var dados = await request.ReadFromJsonAsync<ProdutoDTO>();

    using var conn = Database.GetConnection();
    conn.Open();

    var cmd = conn.CreateCommand();
    cmd.CommandText = "INSERT INTO Produtos (Nome, Preco) VALUES (@n, @p)";
    cmd.Parameters.AddWithValue("@n", dados.Nome);
    cmd.Parameters.AddWithValue("@p", dados.Preco);

    cmd.ExecuteNonQuery();

    return Results.Ok();
});


// =======================
//  COMPRAS
// =======================

app.MapGet("/setup/admin/{telefone}", (string telefone) =>
{
    using var conn = Database.GetConnection();
    conn.Open();

    var cmd = conn.CreateCommand();

    cmd.CommandText = @"
        UPDATE Usuarios
        SET IsAdmin = 1
        WHERE Telefone = @t
    ";

    cmd.Parameters.AddWithValue("@t", telefone);

    int linhas = cmd.ExecuteNonQuery();

    return Results.Ok(new
    {
        atualizado = linhas
    });
});

app.MapPost("/comprar", async (HttpRequest request) =>
{
    var userId = GetUserId(request);

    if (userId == null)
        return Results.Unauthorized();

    var dados =
        await request.ReadFromJsonAsync<List<CompraDTO>>();

    if (dados == null || dados.Count == 0)
        return Results.BadRequest();

    using var conn = Database.GetConnection();
    conn.Open();

    int periodoId = GetPeriodoAbertoId(conn);

    // DATA DA COMPRA
    TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");

    DateTime horarioBrasil = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);

    string dataHora =
        horarioBrasil.ToString("yyyy-MM-dd HH:mm:ss");

    double totalGeral = 0;

    // CALCULA TOTAL
    foreach (var item in dados)
    {
        var precoCmd = conn.CreateCommand();

        precoCmd.CommandText =
            "SELECT Preco FROM Produtos WHERE Id = @id";

        precoCmd.Parameters.AddWithValue("@id", item.ProdutoId);

        var precoObj = precoCmd.ExecuteScalar();

        if (precoObj == null)
            continue;

        double preco =
            Convert.ToDouble(precoObj);

        totalGeral +=
            preco * item.Quantidade;
    }

    // INSERE COMPRA
    var compraCmd = conn.CreateCommand();

    compraCmd.CommandText = @"
        INSERT INTO Compras
        (UsuarioId, DataHora, Total, PeriodoId)
        VALUES
        (@u, @d, @t, @periodo)
    ";

    compraCmd.Parameters.AddWithValue("@u", userId);
    compraCmd.Parameters.AddWithValue("@d", dataHora);
    compraCmd.Parameters.AddWithValue("@t", totalGeral);
    compraCmd.Parameters.AddWithValue("@periodo", periodoId);

    compraCmd.ExecuteNonQuery();

    // PEGA ID DA COMPRA
    var idCmd = conn.CreateCommand();

    idCmd.CommandText =
        "SELECT last_insert_rowid()";

    long compraId =
        (long)idCmd.ExecuteScalar();

    // INSERE ITENS
    foreach (var item in dados)
    {
        var cmd = conn.CreateCommand();

        cmd.CommandText = @"
            INSERT INTO Consumo
            (
                UsuarioId,
                ProdutoId,
                Quantidade,
                DataHora,
                CompraId,
                PeriodoId
            )
            VALUES
            (
                @u,
                @p,
                @q,
                @d,
                @c,
                @periodo
            )
        ";

        cmd.Parameters.AddWithValue("@u", userId);
        cmd.Parameters.AddWithValue("@p", item.ProdutoId);
        cmd.Parameters.AddWithValue("@q", item.Quantidade);
        cmd.Parameters.AddWithValue("@d", dataHora);
        cmd.Parameters.AddWithValue("@c", compraId);
        cmd.Parameters.AddWithValue("@periodo", periodoId);

        cmd.ExecuteNonQuery();
    }

    return Results.Ok(new
    {
        compraId
    });
});

app.MapGet("/comprovante/{id}", (int id, HttpRequest request) =>
{
    var userId = GetUserId(request);

    if (userId == null)
        return Results.Unauthorized();

    using var conn = Database.GetConnection();
    conn.Open();

    // dados compra
    var compraCmd = conn.CreateCommand();

    compraCmd.CommandText = @"
        SELECT
            c.Id,
            c.DataHora,
            c.Total,
            u.Nome
        FROM Compras c
        JOIN Usuarios u ON c.UsuarioId = u.Id
        WHERE c.Id = @id
        AND c.UsuarioId = @u
    ";

    compraCmd.Parameters.AddWithValue("@id", id);
    compraCmd.Parameters.AddWithValue("@u", userId);

    var compraReader = compraCmd.ExecuteReader();

    if (!compraReader.Read())
        return Results.NotFound();

    var nome = compraReader.GetString(3);
    var data = compraReader.GetString(1);
    var total = compraReader.GetDouble(2);

    compraReader.Close();

    // itens
    var itensCmd = conn.CreateCommand();

    itensCmd.CommandText = @"
        SELECT
            p.Nome,
            c.Quantidade,
            p.Preco
        FROM Consumo c
        JOIN Produtos p ON c.ProdutoId = p.Id
        WHERE c.UsuarioId = @u
        AND c.DataHora = @data
    ";

    itensCmd.Parameters.AddWithValue("@u", userId);
    itensCmd.Parameters.AddWithValue("@data", data);

    var itensReader = itensCmd.ExecuteReader();

    var itens = new List<object>();

    while (itensReader.Read())
    {
        itens.Add(new
        {
            produto = itensReader.GetString(0),
            qtd = itensReader.GetInt32(1),
            preco = itensReader.GetDouble(2)
        });
    }

    return Results.Ok(new
    {
        id,
        nome,
        data,
        total,
        itens
    });
});

// =======================
// HISTÓRICO USER
// =======================

app.MapGet("/historico/{periodoId}", (int periodoId, HttpRequest request) =>
{
    var userId = GetUserId(request);

    if (userId == null)
        return Results.Unauthorized();

    using var conn = Database.GetConnection();
    conn.Open();

    var cmd = conn.CreateCommand();

    cmd.CommandText = @"
        SELECT
            p.Nome,
            c.Quantidade,
            p.Preco,
            c.DataHora,
            a.Nome
        FROM Consumo c
        JOIN Produtos p ON c.ProdutoId = p.Id
        LEFT JOIN Usuarios a ON c.AdminId = a.Id
        WHERE c.UsuarioId = @u
        AND c.PeriodoId = @periodo
        ORDER BY c.DataHora DESC";

    cmd.Parameters.AddWithValue("@u", userId);
    cmd.Parameters.AddWithValue("@periodo", periodoId);

    var reader = cmd.ExecuteReader();

    var lista = new List<object>();
    double total = 0;

    while (reader.Read())
    {
        double itemTotal = reader.GetInt32(1) * reader.GetDouble(2);
        total += itemTotal;

        lista.Add(new
        {
            produto = reader.GetString(0),
            qtd = reader.GetInt32(1),
            data = reader.GetString(3),
            total = itemTotal,
            admin = reader.IsDBNull(4) ? null : reader.GetString(4)
        });
    }

    return Results.Ok(new
    {
        itens = lista,
        total
    });
});

app.MapGet("/periodos", (HttpRequest request) =>
{
    var userId = GetUserId(request);

    if (userId == null)
        return Results.Unauthorized();

    using var conn = Database.GetConnection();
    conn.Open();

    var cmd = conn.CreateCommand();

    cmd.CommandText = @"
        SELECT Id, Nome, Fechado
        FROM Periodos
        ORDER BY Ano DESC, Mes DESC";

    var reader = cmd.ExecuteReader();

    var lista = new List<object>();

    while(reader.Read())
    {
        lista.Add(new
        {
            id = reader.GetInt32(0),
            nome = reader.GetString(1),
            fechado = reader.GetInt32(2) == 1
        });
    }

    return Results.Ok(lista);
});

// =======================
//  ADMIN
// =======================
//provisiorio===========
app.MapGet("/admin/mensagens-cobranca/{periodoId}",
(int periodoId, HttpRequest request) =>
{
    if (!IsAdmin(request))
        return Results.Unauthorized();

    using var conn = Database.GetConnection();
    conn.Open();

    // Busca o nome do período
    var periodoCmd = conn.CreateCommand();

    periodoCmd.CommandText = @"
        SELECT Nome
        FROM Periodos
        WHERE Id = @id";

    periodoCmd.Parameters.AddWithValue("@id", periodoId);

    var periodoObj = periodoCmd.ExecuteScalar();

    if (periodoObj == null)
        return Results.NotFound("Mês não encontrado.");

    string nomePeriodo = periodoObj.ToString() ?? "periodo";

    // Busca todos os clientes com dívida no mês
    var cmd = conn.CreateCommand();

    cmd.CommandText = @"
        SELECT
            u.Id,
            u.Nome,
            u.Posto,
            u.Telefone,
            SUM(c.Quantidade * p.Preco) AS Total
        FROM Consumo c
        JOIN Usuarios u
            ON c.UsuarioId = u.Id
        JOIN Produtos p
            ON c.ProdutoId = p.Id
        WHERE c.PeriodoId = @periodo
        GROUP BY
            u.Id,
            u.Nome,
            u.Posto,
            u.Telefone
        HAVING SUM(c.Quantidade * p.Preco) > 0
        ORDER BY u.Posto, u.Nome";

    cmd.Parameters.AddWithValue("@periodo", periodoId);

    var reader = cmd.ExecuteReader();

    var texto = new StringBuilder();

    texto.AppendLine("========================================");
    texto.AppendLine("MENSAGENS DE COBRANÇA");
    texto.AppendLine($"PERÍODO: {nomePeriodo}");
    texto.AppendLine("========================================");
    texto.AppendLine();

    int quantidadeClientes = 0;
    double valorTotal = 0;

    while (reader.Read())
    {
        int usuarioId = reader.GetInt32(0);

        string nome =
            reader.IsDBNull(1)
                ? ""
                : reader.GetString(1);

        string posto =
            reader.IsDBNull(2)
                ? ""
                : reader.GetString(2);

        string telefone =
            reader.IsDBNull(3)
                ? ""
                : reader.GetString(3);

        double total = reader.GetDouble(4);

        quantidadeClientes++;
        valorTotal += total;

        string nomeCompleto =
            $"{posto} {nome}".Trim();

        string mensagem =
            $"Bom dia {nomeCompleto}!\n" +
            $"Segue o valor do que consumiu na CCAP durante o mês de {nomePeriodo}.\n" +
            $"Valor: R$ {total:F2}.\n\n" +
            $"Pix: matheusmatft@gmail.com\n\n" +
            $"*Banco NEON*\n\n" +
            $"FAVOR ENVIAR O COMPROVANTE APÓS O PAGAMENTO\n" +
            $"Obs: Caso tenha alguma dúvida sobre valores ou algum item, " +
            $"só mencionar que verificamos.";

        texto.AppendLine("----------------------------------------");
        texto.AppendLine($"CLIENTE #{quantidadeClientes}");
        texto.AppendLine($"ID: {usuarioId}");
        texto.AppendLine($"CLIENTE: {nomeCompleto}");
        texto.AppendLine($"TELEFONE: {telefone}");
        texto.AppendLine($"VALOR: R$ {total:F2}");
        texto.AppendLine("----------------------------------------");
        texto.AppendLine();
        texto.AppendLine(mensagem);
        texto.AppendLine();
        texto.AppendLine();
    }

    reader.Close();

    texto.AppendLine("========================================");
    texto.AppendLine("RESUMO");
    texto.AppendLine($"CLIENTES COM DÍVIDA: {quantidadeClientes}");
    texto.AppendLine($"VALOR TOTAL: R$ {valorTotal:F2}");
    texto.AppendLine("========================================");

    if (quantidadeClientes == 0)
        return Results.NotFound(
            "Nenhum cliente com dívida no mês selecionado."
        );

    string nomeSeguro = nomePeriodo
        .Replace("/", "-")
        .Replace("\\", "-")
        .Replace(" ", "-");

    string nomeArquivo =
        $"mensagens-cobranca-{nomeSeguro}.txt";

    byte[] arquivo =
        Encoding.UTF8.GetPreamble()
        .Concat(Encoding.UTF8.GetBytes(texto.ToString()))
        .ToArray();

    return Results.File(
        arquivo,
        contentType: "text/plain; charset=utf-8",
        fileDownloadName: nomeArquivo
    );
});

app.MapGet("/admin/clientes", (HttpRequest request) =>
{
    if (!IsAdmin(request)) return Results.Unauthorized();

    using var conn = Database.GetConnection();
    conn.Open();

    var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT Id, Nome, Posto, Telefone FROM Usuarios";

    var reader = cmd.ExecuteReader();
    var lista = new List<object>();

    while (reader.Read())
    {
        lista.Add(new
        {
            id = reader.GetInt32(0),
            nome = reader.GetString(1),
            posto = reader.IsDBNull(2) ? "" : reader.GetString(2),
            telefone = reader.IsDBNull(3) ? "" : reader.GetString(3)
        });
    }

    return Results.Ok(lista);
});

app.MapGet("/admin/historico/{id}/{periodoId}", (int id, int periodoId, HttpRequest request) =>
{
    if (!IsAdmin(request))
        return Results.Unauthorized();

    using var conn = Database.GetConnection();
    conn.Open();

    var cmd = conn.CreateCommand();

    cmd.CommandText = @"
        SELECT
            c.Id,
            p.Nome,
            c.Quantidade,
            c.DataHora,
            a.Nome,
            p.Preco
        FROM Consumo c
        JOIN Produtos p ON c.ProdutoId = p.Id
        LEFT JOIN Usuarios a ON c.AdminId = a.Id
        WHERE c.UsuarioId = @id
        AND c.PeriodoId = @periodo
        ORDER BY c.DataHora DESC";

    cmd.Parameters.AddWithValue("@id", id);
    cmd.Parameters.AddWithValue("@periodo", periodoId);

    var reader = cmd.ExecuteReader();

    var lista = new List<object>();
    double total = 0;

    while (reader.Read())
    {
        int qtd = reader.GetInt32(2);
        double preco = reader.GetDouble(5);
        double itemTotal = qtd * preco;

        total += itemTotal;

        lista.Add(new
        {
            id = reader.GetInt32(0),
            produto = reader.GetString(1),
            qtd = qtd,
            data = reader.GetString(3),
            total = itemTotal,

            admin = reader.IsDBNull(4)
                ? null
                : reader.GetString(4)
        });
    }

    return Results.Ok(new
    {
        itens = lista,
        total
    });
});

app.MapDelete("/admin/remover/{id}", (int id, HttpRequest request) =>
{
    if (!IsAdmin(request)) return Results.Unauthorized();

    using var conn = Database.GetConnection();
    conn.Open();

    var cmd = conn.CreateCommand();
    cmd.CommandText = "DELETE FROM Consumo WHERE Id = @id";
    cmd.Parameters.AddWithValue("@id", id);

    cmd.ExecuteNonQuery();

    return Results.Ok();
});

app.MapDelete("/admin/zerar/{userId}/{periodoId}",
(int userId, int periodoId, HttpRequest request) =>
{
    if (!IsAdmin(request))
        return Results.Unauthorized();

    using var conn = Database.GetConnection();
    conn.Open();

    var cmd = conn.CreateCommand();

    cmd.CommandText = @"
        DELETE FROM Consumo
        WHERE UsuarioId = @u
        AND PeriodoId = @p";

    cmd.Parameters.AddWithValue("@u", userId);
    cmd.Parameters.AddWithValue("@p", periodoId);

    int linhas = cmd.ExecuteNonQuery();

    return Results.Ok(
        $"Registros removidos: {linhas}"
    );
});

app.MapGet("/admin/dashboard/{periodoId}", (int periodoId, HttpRequest request) =>
{
    if (!IsAdmin(request))
        return Results.Unauthorized();

    using var conn = Database.GetConnection();
    conn.Open();

    var cmd = conn.CreateCommand();

    cmd.CommandText = @"
        SELECT COUNT(DISTINCT UsuarioId)
        FROM Consumo
        WHERE PeriodoId = @periodo";

    cmd.Parameters.AddWithValue("@periodo", periodoId);

    int clientesDevendo = Convert.ToInt32(cmd.ExecuteScalar());

    var totalCmd = conn.CreateCommand();

    totalCmd.CommandText = @"
        SELECT SUM(c.Quantidade * p.Preco)
        FROM Consumo c
        JOIN Produtos p ON c.ProdutoId = p.Id
        WHERE c.PeriodoId = @periodo";

    totalCmd.Parameters.AddWithValue("@periodo", periodoId);

    var totalObj = totalCmd.ExecuteScalar();

    double total = totalObj != DBNull.Value && totalObj != null
        ? Convert.ToDouble(totalObj)
        : 0;

    return Results.Ok(new
    {
        clientesDevendo,
        total
    });
});

app.MapPost("/admin/cobrar-clientes/{periodoId}", async (int periodoId, HttpRequest request) =>
{
    if (!IsAdmin(request))
        return Results.Unauthorized();

    using var conn = Database.GetConnection();
    conn.Open();

    var cmd = conn.CreateCommand();

    cmd.CommandText = @"
        SELECT 
            u.Id,
            u.Nome,
            u.Posto,
            u.Telefone,
            SUM(c.Quantidade * p.Preco) AS Total
        FROM Consumo c
        JOIN Usuarios u ON c.UsuarioId = u.Id
        JOIN Produtos p ON c.ProdutoId = p.Id
        WHERE c.PeriodoId = @periodo
        GROUP BY u.Id, u.Nome, u.Posto, u.Telefone
        HAVING Total > 0";

    cmd.Parameters.AddWithValue("@periodo", periodoId);

    var reader = cmd.ExecuteReader();

    var clientes = new List<dynamic>();

    while (reader.Read())
    {
        clientes.Add(new
        {
            Nome = reader.GetString(1),
            Posto = reader.IsDBNull(2) ? "" : reader.GetString(2),
            Telefone = "55" + reader.GetString(3),
            Total = reader.GetDouble(4)
        });
    }

    reader.Close();

    int enviados = 0;
    Random random = new Random();

    foreach (dynamic cliente in clientes)
    {
        string mensagem =
            $"Bom dia {cliente.Posto} {cliente.Nome}!\n" +
            $"Segue o valor do que consumiu na CCAP durante o mês anterior\n" +
            $"Valor: R$ {cliente.Total:F2}.\n\n" +
            $"Pix: matheusmatft@gmail.com\n\n" +
            $"*Banco NEON*\n\n"+
            $"FAVOR ENVIAR O COMPROVANTE APÓS O PAGAMENTO\n"+
            $"Obs: Caso tenha alguma dúvida sobre valores ou algum item, só mencionar que verificamos assinatura.";

        bool enviado = await EnviarWhatsApp(cliente.Telefone, mensagem);

        if (enviado)
            enviados++;

        await Task.Delay(random.Next(8000, 18000));
    }

    return Results.Ok($"Cobranças enviadas: {enviados}");
});

app.MapGet("/admin/clientes-devendo/{periodoId}", (int periodoId, HttpRequest request) =>
{
    if (!IsAdmin(request))
        return Results.Unauthorized();

    using var conn = Database.GetConnection();
    conn.Open();

    var cmd = conn.CreateCommand();

    cmd.CommandText = @"
        SELECT 
            u.Id,
            u.Nome,
            u.Posto,
            u.Telefone,
            SUM(c.Quantidade * p.Preco) AS Total
        FROM Consumo c
        JOIN Usuarios u ON c.UsuarioId = u.Id
        JOIN Produtos p ON c.ProdutoId = p.Id
        WHERE c.PeriodoId = @periodo
        GROUP BY u.Id, u.Nome, u.Posto, u.Telefone
        HAVING Total > 0";

    cmd.Parameters.AddWithValue("@periodo", periodoId);

    var reader = cmd.ExecuteReader();
    var lista = new List<object>();

    while(reader.Read())
    {
        lista.Add(new
        {
            id = reader.GetInt32(0),
            nome = reader.GetString(1),
            posto = reader.IsDBNull(2) ? "" : reader.GetString(2),
            telefone = reader.GetString(3),
            total = reader.GetDouble(4)
        });
    }

    return Results.Ok(lista);
});

app.MapPost("/admin/cobrar-cliente", async (CobrarClienteDTO dto, HttpRequest request) =>
{
    if (!IsAdmin(request))
        return Results.Unauthorized();

    using var conn = Database.GetConnection();
    conn.Open();

    var cmd = conn.CreateCommand();

    cmd.CommandText = @"
        SELECT 
            u.Nome,
            u.Posto,
            u.Telefone,
            SUM(c.Quantidade * p.Preco) AS Total
        FROM Consumo c
        JOIN Usuarios u ON c.UsuarioId = u.Id
        JOIN Produtos p ON c.ProdutoId = p.Id
        WHERE c.UsuarioId = @usuario
        AND c.PeriodoId = @periodo
        GROUP BY u.Nome, u.Posto, u.Telefone";

    cmd.Parameters.AddWithValue("@usuario", dto.UsuarioId);
    cmd.Parameters.AddWithValue("@periodo", dto.PeriodoId);

    var reader = cmd.ExecuteReader();

    if(!reader.Read())
        return Results.NotFound();

    string nome = reader.GetString(0);
    string posto = reader.IsDBNull(1) ? "" : reader.GetString(1);
    string telefone = "55" + reader.GetString(2);
    double total = reader.GetDouble(3);

    string mensagem =
        $"Bom dia {posto} {nome}!\n" +
        $"Segue o valor do que consumiu na CCAP durante o mês anterior\n" +
        $"Valor: R$ {total:F2}.\n\n" +
        $"Pix: matheusmatft@gmail.com\n\n" +
        $"*Banco NEON*\n\n" +
        $"FAVOR ENVIAR O COMPROVANTE APÓS O PAGAMENTO\n" +
        $"Obs: Caso tenha alguma dúvida sobre valores ou algum item, só mencionar que verificamos.";

    bool enviado = await EnviarWhatsApp(telefone, mensagem);

    if(!enviado)
        return Results.BadRequest("Erro ao enviar mensagem.");

    return Results.Ok();
});

app.MapGet("/admin/periodos", (HttpRequest request) =>
{
    if (!IsAdmin(request))
        return Results.Unauthorized();

    using var conn = Database.GetConnection();
    conn.Open();

    var cmd = conn.CreateCommand();

    cmd.CommandText = @"
        SELECT Id, Nome, Mes, Ano, Fechado
        FROM Periodos
        ORDER BY Ano DESC, Mes DESC";

    var reader = cmd.ExecuteReader();

    var lista = new List<object>();

    while (reader.Read())
    {
        lista.Add(new
        {
            id = reader.GetInt32(0),
            nome = reader.GetString(1),
            mes = reader.GetInt32(2),
            ano = reader.GetInt32(3),
            fechado = reader.GetInt32(4) == 1
        });
    }

    return Results.Ok(lista);
});

app.MapPost("/admin/fechar-mes", (HttpRequest request) =>
{
    if (!IsAdmin(request))
        return Results.Unauthorized();

    using var conn = Database.GetConnection();
    conn.Open();

    var atualCmd = conn.CreateCommand();

    atualCmd.CommandText = @"
        SELECT Id, Mes, Ano
        FROM Periodos
        WHERE Fechado = 0
        ORDER BY Ano DESC, Mes DESC
        LIMIT 1";

    var reader = atualCmd.ExecuteReader();

    if (!reader.Read())
        return Results.BadRequest("Nenhum mês aberto encontrado.");

    int periodoId = reader.GetInt32(0);
    int mes = reader.GetInt32(1);
    int ano = reader.GetInt32(2);

    reader.Close();

    var fecharCmd = conn.CreateCommand();

    fecharCmd.CommandText = @"
        UPDATE Periodos
        SET Fechado = 1
        WHERE Id = @id";

    fecharCmd.Parameters.AddWithValue("@id", periodoId);
    fecharCmd.ExecuteNonQuery();

    mes++;

    if (mes > 12)
    {
        mes = 1;
        ano++;
    }



    string nome = new DateTime(ano, mes, 1)
        .ToString("MMMM/yyyy", new System.Globalization.CultureInfo("pt-BR"));

    var criarCmd = conn.CreateCommand();

    criarCmd.CommandText = @"
        INSERT INTO Periodos
        (Nome, Mes, Ano, Fechado)
        VALUES
        (@nome, @mes, @ano, 0)";

    criarCmd.Parameters.AddWithValue("@nome", nome);
    criarCmd.Parameters.AddWithValue("@mes", mes);
    criarCmd.Parameters.AddWithValue("@ano", ano);

    criarCmd.ExecuteNonQuery();

    return Results.Ok("Mês fechado com sucesso.");
});

app.MapGet("/admin/relatorio/produto/{produtoId}/{periodoId}",
(int produtoId, int periodoId, HttpRequest request) =>
{
    if (!IsAdmin(request))
        return Results.Unauthorized();

    using var conn = Database.GetConnection();
    conn.Open();

    var cmd = conn.CreateCommand();

    cmd.CommandText = @"
        SELECT
            IFNULL(SUM(c.Quantidade), 0),
            IFNULL(SUM(c.Quantidade * p.Preco), 0)
        FROM Consumo c
        JOIN Produtos p ON c.ProdutoId = p.Id
        WHERE c.ProdutoId = @produto
        AND c.PeriodoId = @periodo
    ";

    cmd.Parameters.AddWithValue("@produto", produtoId);
    cmd.Parameters.AddWithValue("@periodo", periodoId);

    var reader = cmd.ExecuteReader();

    if (!reader.Read())
    {
        return Results.Ok(new
        {
            quantidade = 0,
            total = 0.0
        });
    }

    return Results.Ok(new
    {
        quantidade = reader.GetInt32(0),
        total = reader.GetDouble(1)
    });
});

app.MapPut("/admin/usuario/{id}", async (int id, HttpRequest request) =>
{
    if (!IsAdmin(request))
        return Results.Unauthorized();

    var dto = await request.ReadFromJsonAsync<AtualizarUsuarioDTO>();

    if (dto == null)
        return Results.BadRequest("Dados inválidos.");

    string telefone = new string(
        dto.Telefone
            .Where(char.IsDigit)
            .ToArray()
    );

    using var conn = Database.GetConnection();
    conn.Open();

    var cmd = conn.CreateCommand();

    cmd.CommandText = @"
        UPDATE Usuarios
        SET
            Nome = @nome,
            Posto = @posto,
            Telefone = @telefone
        WHERE Id = @id";

    cmd.Parameters.AddWithValue("@nome", dto.Nome);
    cmd.Parameters.AddWithValue("@posto", dto.Posto);
    cmd.Parameters.AddWithValue("@telefone", telefone);
    cmd.Parameters.AddWithValue("@id", id);

    int linhas = cmd.ExecuteNonQuery();

    if(linhas == 0)
        return Results.NotFound("Usuário não encontrado.");

    return Results.Ok("Usuário atualizado.");
});

app.MapDelete("/admin/usuario/{id}", (int id, HttpRequest request) =>
{
    if (!IsAdmin(request))
        return Results.Unauthorized();

    using var conn = Database.GetConnection();
    conn.Open();

    // Remove consumos do usuário
    var consumoCmd = conn.CreateCommand();
    consumoCmd.CommandText = "DELETE FROM Consumo WHERE UsuarioId = @id";
    consumoCmd.Parameters.AddWithValue("@id", id);
    consumoCmd.ExecuteNonQuery();

    // Remove compras do usuário
    var comprasCmd = conn.CreateCommand();
    comprasCmd.CommandText = "DELETE FROM Compras WHERE UsuarioId = @id";
    comprasCmd.Parameters.AddWithValue("@id", id);
    comprasCmd.ExecuteNonQuery();

    // Remove usuário
    var usuarioCmd = conn.CreateCommand();
    usuarioCmd.CommandText = "DELETE FROM Usuarios WHERE Id = @id";
    usuarioCmd.Parameters.AddWithValue("@id", id);

    int linhas = usuarioCmd.ExecuteNonQuery();

    if(linhas == 0)
        return Results.NotFound("Usuário não encontrado.");

    return Results.Ok("Usuário excluído.");
});

app.MapPost("/admin/adicionar", async (HttpRequest request) =>
{
    string token = request.Headers["Authorization"];

    using var conn = Database.GetConnection();
    conn.Open();

    // validar admin
    var cmdAdmin = conn.CreateCommand();
    cmdAdmin.CommandText =
        "SELECT Id, Nome, IsAdmin FROM Usuarios WHERE Token = @t";

    cmdAdmin.Parameters.AddWithValue("@t", token);

    var reader = cmdAdmin.ExecuteReader();

    if (!reader.Read() || reader.GetInt32(2) == 0)
        return Results.Unauthorized();

    int adminId = reader.GetInt32(0);

    reader.Close();

    // receber dados
    var dto = await request.ReadFromJsonAsync<CompraAdminDTO>();

    var cmd = conn.CreateCommand();

    cmd.CommandText = @"
        INSERT INTO Consumo
        (UsuarioId, ProdutoId, Quantidade, DataHora, AdminId, PeriodoId)
        VALUES
        (@u, @p, @q, @d, @a, @periodo)";

    TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");

    DateTime horarioBrasil = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);

    cmd.Parameters.AddWithValue("@u", dto.UsuarioId);
    cmd.Parameters.AddWithValue("@p", dto.ProdutoId);
    cmd.Parameters.AddWithValue("@q", dto.Quantidade);
    cmd.Parameters.AddWithValue("@d",
        horarioBrasil.ToString("yyyy-MM-dd HH:mm:ss"));

    cmd.Parameters.AddWithValue("@a", adminId);
    cmd.Parameters.AddWithValue("@periodo", dto.PeriodoId);

    cmd.ExecuteNonQuery();

    return Results.Ok();
});

async Task<bool> EnviarWhatsApp(string numero, string mensagem)
{
    using var client = new HttpClient();

    string evolutionUrl = Environment.GetEnvironmentVariable("EVOLUTION_URL");
    string evolutionInstance = Environment.GetEnvironmentVariable("EVOLUTION_INSTANCE");
    string evolutionApiKey = Environment.GetEnvironmentVariable("EVOLUTION_API_KEY");


    string url = $"{evolutionUrl}/message/sendText/{evolutionInstance}";

    var json = new
    {
        number = numero,
        text = mensagem
    };

    var content = new StringContent(
        System.Text.Json.JsonSerializer.Serialize(json),
        Encoding.UTF8,
        "application/json"
    );

    client.DefaultRequestHeaders.Add("apikey", evolutionApiKey);

    var response = await client.PostAsync(url, content);

    string resposta = await response.Content.ReadAsStringAsync();

    return response.IsSuccessStatusCode;
}

app.Run();

record CobrarClienteDTO(int UsuarioId, int PeriodoId);
record AtualizarUsuarioDTO(string Nome,string Posto,string Telefone);
