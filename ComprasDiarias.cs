using Microsoft.Data.Sqlite;

public static class ComprasDiarias
{
    public record Item(long Id, long? CompraId, int UsuarioId, int PeriodoId, int ProdutoId,
        int Quantidade, string Data, string Nome, string Posto, string Produto, double Total, double Pago);

    private static SqliteCommand Cmd(SqliteConnection conn, SqliteTransaction? tx, string sql,
        params (string, object)[] values)
    {
        var cmd = conn.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = sql;
        foreach (var (key, value) in values) cmd.Parameters.AddWithValue(key, value);
        return cmd;
    }

    public static List<Item> Ler(SqliteConnection conn, SqliteTransaction? tx, string filtro,
        params (string, object)[] values)
    {
        using var cmd = Cmd(conn, tx, @"SELECT c.Id, c.CompraId, c.UsuarioId, c.PeriodoId, c.ProdutoId,
            c.Quantidade, c.DataHora, COALESCE(u.Nome, 'Cliente removido'), COALESCE(u.Posto, ''),
            COALESCE(p.Nome, 'Produto removido'), ROUND(c.Quantidade * COALESCE(p.Preco, 0), 2),
            COALESCE(pi.ValorPago, 0)
            FROM Consumo c LEFT JOIN Usuarios u ON u.Id = c.UsuarioId
            LEFT JOIN Produtos p ON p.Id = c.ProdutoId
            LEFT JOIN PagamentosItens pi ON pi.ConsumoId = c.Id
            WHERE " + filtro + " ORDER BY c.DataHora, c.Id", values);
        using var r = cmd.ExecuteReader(); var itens = new List<Item>();
        while (r.Read()) itens.Add(new Item(r.GetInt64(0), r.IsDBNull(1) ? null : r.GetInt64(1),
            r.GetInt32(2), r.GetInt32(3), r.GetInt32(4), r.GetInt32(5), r.GetString(6),
            r.GetString(7), r.GetString(8), r.GetString(9), r.GetDouble(10), r.GetDouble(11)));
        return itens;
    }

    private static void RegistrarItem(SqliteConnection conn, SqliteTransaction tx, long id, double valor)
    {
        using var cmd = Cmd(conn, tx, @"INSERT INTO PagamentosItens (ConsumoId, ValorPago) VALUES (@id, @valor)
            ON CONFLICT(ConsumoId) DO UPDATE SET ValorPago = excluded.ValorPago", ("@id", id), ("@valor", valor));
        cmd.ExecuteNonQuery();
    }

    // Distribui os pagamentos anteriores apenas uma vez, sem alterar seu valor mensal.
    public static void Inicializar(SqliteConnection conn)
    {
        using var tx = conn.BeginTransaction();
        using (var cmd = Cmd(conn, tx, @"CREATE TABLE IF NOT EXISTS PagamentosItens
            (ConsumoId INTEGER PRIMARY KEY, ValorPago REAL NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS MigracoesApp (Nome TEXT PRIMARY KEY);
            CREATE INDEX IF NOT EXISTS IX_Consumo_DataHora ON Consumo(DataHora);
            CREATE INDEX IF NOT EXISTS IX_Consumo_CompraId ON Consumo(CompraId);")) cmd.ExecuteNonQuery();
        using var check = Cmd(conn, tx, "SELECT COUNT(*) FROM MigracoesApp WHERE Nome = 'pagamentos-itens-v1'");
        if (Convert.ToInt32(check.ExecuteScalar()) == 0)
        {
            var meses = new List<(int Usuario, int Periodo, double Valor)>();
            using (var cmd = Cmd(conn, tx, "SELECT UsuarioId, PeriodoId, ValorPago FROM PagamentosMensais WHERE MarcadoPago = 1"))
            using (var r = cmd.ExecuteReader())
                while (r.Read()) meses.Add((r.GetInt32(0), r.GetInt32(1), r.GetDouble(2)));
            foreach (var mes in meses)
            {
                var restante = mes.Valor;
                foreach (var item in Ler(conn, tx, "c.UsuarioId = @u AND c.PeriodoId = @p", ("@u", mes.Usuario), ("@p", mes.Periodo)))
                {
                    var valor = Math.Min(Math.Max(0, item.Total), restante);
                    if (valor > 0) RegistrarItem(conn, tx, item.Id, valor);
                    restante = Math.Max(0, Math.Round(restante - valor, 2));
                }
            }
            using var done = Cmd(conn, tx, "INSERT INTO MigracoesApp VALUES ('pagamentos-itens-v1')"); done.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private static void AjustarMes(SqliteConnection conn, SqliteTransaction tx, int usuario, int periodo, double delta, int admin)
    {
        using var cmd = Cmd(conn, tx, @"INSERT INTO PagamentosMensais
            (UsuarioId, PeriodoId, MarcadoPago, ValorPago, AtualizadoEm, AdminId)
            VALUES (@u, @p, 1, MAX(0, @delta), @data, @admin)
            ON CONFLICT(UsuarioId, PeriodoId) DO UPDATE SET
                ValorPago = MAX(0, ROUND(CASE WHEN MarcadoPago = 1 THEN ValorPago ELSE 0 END + @delta, 2)),
                MarcadoPago = 1, AtualizadoEm = @data, AdminId = @admin",
            ("@u", usuario), ("@p", periodo), ("@delta", delta), ("@data", DateTime.UtcNow.ToString("O")), ("@admin", admin));
        cmd.ExecuteNonQuery();
        using var limpar = Cmd(conn, tx, "UPDATE PagamentosMensais SET MarcadoPago = 0 WHERE UsuarioId = @u AND PeriodoId = @p AND ValorPago <= 0",
            ("@u", usuario), ("@p", periodo)); limpar.ExecuteNonQuery();
    }

    public static void Pagar(SqliteConnection conn, SqliteTransaction tx, List<Item> itens, bool pago, int admin)
    {
        foreach (var grupo in itens.GroupBy(i => (i.UsuarioId, i.PeriodoId)))
        {
            double delta = 0;
            foreach (var item in grupo)
            {
                var valor = pago ? Math.Max(item.Pago, item.Total) : 0;
                RegistrarItem(conn, tx, item.Id, valor); delta += valor - item.Pago;
            }
            AjustarMes(conn, tx, grupo.Key.UsuarioId, grupo.Key.PeriodoId, delta, admin);
        }
    }

    public static double SincronizarMes(SqliteConnection conn, SqliteTransaction tx, int usuario, int periodo, bool pago)
    {
        var itens = Ler(conn, tx, "c.UsuarioId = @u AND c.PeriodoId = @p", ("@u", usuario), ("@p", periodo));
        foreach (var item in itens) RegistrarItem(conn, tx, item.Id, pago ? Math.Max(item.Total, item.Pago) : 0);
        return pago ? itens.Sum(item => Math.Max(item.Total, item.Pago)) : 0;
    }

    public static void Remover(SqliteConnection conn, SqliteTransaction tx, List<Item> itens, int admin)
    {
        // Estorna somente o pagamento dos itens removidos e devolve suas quantidades ao estoque.
        Pagar(conn, tx, itens, false, admin);
        foreach (var item in itens)
        {
            using var cmd = Cmd(conn, tx, @"DELETE FROM PagamentosItens WHERE ConsumoId = @id;
                DELETE FROM Consumo WHERE Id = @id;
                INSERT INTO Estoque (ProdutoId, Quantidade) VALUES (@produto, @q)
                ON CONFLICT(ProdutoId) DO UPDATE SET Quantidade = Quantidade + @q;",
                ("@id", item.Id), ("@produto", item.ProdutoId), ("@q", item.Quantidade)); cmd.ExecuteNonQuery();
        }
        foreach (var compra in itens.Where(i => i.CompraId.HasValue).Select(i => i.CompraId!.Value).Distinct())
        {
            using var cmd = Cmd(conn, tx, @"UPDATE Compras SET Total = COALESCE((SELECT SUM(c.Quantidade * p.Preco)
                FROM Consumo c JOIN Produtos p ON p.Id = c.ProdutoId WHERE c.CompraId = @id), 0) WHERE Id = @id;
                DELETE FROM Compras WHERE Id = @id AND NOT EXISTS(SELECT 1 FROM Consumo WHERE CompraId = @id);", ("@id", compra));
            cmd.ExecuteNonQuery();
        }
    }
}
