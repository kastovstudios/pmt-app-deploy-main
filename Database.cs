using Microsoft.Data.Sqlite;

public static class Database
{
    private static string connectionString =
    "Data Source=/app/data/banco.db";
    public static SqliteConnection GetConnection()
    {
        return new SqliteConnection(connectionString);
    }

    public static void Inicializar()
    {
        using var conn = GetConnection();
        conn.Open();

        var cmd = conn.CreateCommand();

        cmd.CommandText = @"
        CREATE TABLE IF NOT EXISTS Usuarios (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Nome TEXT,
            Telefone TEXT UNIQUE,
            SenhaHash TEXT,
            Token TEXT,
            Posto TEXT,
            IsAdmin INTEGER DEFAULT 0,
            PrecisaTrocarSenha INTEGER NOT NULL DEFAULT 0
        );";

        cmd.CommandText += @"
        CREATE TABLE IF NOT EXISTS Produtos (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Nome TEXT,
            Preco REAL
        );

        CREATE TABLE IF NOT EXISTS Consumo (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            CompraId INTEGER,
            UsuarioId INTEGER,
            ProdutoId INTEGER,
            Quantidade INTEGER,
            DataHora TEXT,
            AdminId INTEGER NULL,
            PeriodoId INTEGER
        );

        CREATE TABLE IF NOT EXISTS Compras (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            UsuarioId INTEGER,
            DataHora TEXT,
            Total REAL,
            PeriodoId INTEGER
        );

        CREATE TABLE IF NOT EXISTS Periodos (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Nome TEXT,
            Mes INTEGER,
            Ano INTEGER,
            Fechado INTEGER DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS Estoque (
            ProdutoId INTEGER PRIMARY KEY,
            Quantidade INTEGER NOT NULL DEFAULT 0,
            FOREIGN KEY (ProdutoId) REFERENCES Produtos(Id)
        );
        ";

        cmd.ExecuteNonQuery();

        // Migração para bancos já existentes: adiciona o controle de troca obrigatória de senha.
        var verificarColuna = conn.CreateCommand();
        verificarColuna.CommandText = "PRAGMA table_info(Usuarios)";

        bool temPrecisaTrocarSenha = false;
        using (var reader = verificarColuna.ExecuteReader())
        {
            while (reader.Read())
            {
                if (string.Equals(reader["name"]?.ToString(), "PrecisaTrocarSenha", StringComparison.OrdinalIgnoreCase))
                {
                    temPrecisaTrocarSenha = true;
                    break;
                }
            }
        }

        if (!temPrecisaTrocarSenha)
        {
            var migracao = conn.CreateCommand();
            migracao.CommandText = "ALTER TABLE Usuarios ADD COLUMN PrecisaTrocarSenha INTEGER NOT NULL DEFAULT 0";
            migracao.ExecuteNonQuery();
        }

        // Exclusão do catálogo sem remover os vínculos dos consumos anteriores.
        using var verificarProduto = conn.CreateCommand();
        verificarProduto.CommandText = "PRAGMA table_info(Produtos)";
        bool temExcluido = false;
        using (var readerProduto = verificarProduto.ExecuteReader())
        {
            while (readerProduto.Read())
                if (string.Equals(readerProduto["name"]?.ToString(), "Excluido", StringComparison.OrdinalIgnoreCase))
                    temExcluido = true;
        }
        if (!temExcluido)
        {
            using var migrarProduto = conn.CreateCommand();
            migrarProduto.CommandText = "ALTER TABLE Produtos ADD COLUMN Excluido INTEGER NOT NULL DEFAULT 0";
            migrarProduto.ExecuteNonQuery();
        }

        using var pagamentos = conn.CreateCommand();
        pagamentos.CommandText = @"
            CREATE TABLE IF NOT EXISTS PagamentosMensais (
                UsuarioId INTEGER NOT NULL,
                PeriodoId INTEGER NOT NULL,
                MarcadoPago INTEGER NOT NULL DEFAULT 0,
                ValorPago REAL NOT NULL DEFAULT 0,
                AtualizadoEm TEXT NOT NULL,
                AdminId INTEGER NOT NULL,
                PRIMARY KEY (UsuarioId, PeriodoId)
            )";
        pagamentos.ExecuteNonQuery();
    }
}
