using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;

namespace FTO_App
{
    // Classe Modelo de Venda (Mantida original para compatibilidade)
    public class Venda
    {
        public long Id { get; set; }
        public string Cliente { get; set; } = string.Empty;
        public string Contato { get; set; } = string.Empty;
        public DateTime Data { get; set; }
        public decimal Gastos { get; set; }
        public decimal VendaValor { get; set; }
        public decimal Lucros => VendaValor - Gastos;
        public string TipoServico { get; set; } = string.Empty;
        public string FormaPag { get; set; } = string.Empty;
        public string Pago { get; set; } = string.Empty;
        public string CPF_CNPJ { get; set; } = string.Empty;

        public string DataFormatada => Data.ToString("dd/MM/yyyy");
        public string GastosFormatado => Gastos.ToString("C2");
        public string VendaFormatada => VendaValor.ToString("C2");
        public string LucrosFormatado => Lucros.ToString("C2");
    }

    public class Database
    {
        // --- CORREÇÃO DE CAMINHO (Path) ---
        // Esta lógica impede o erro "Value cannot be null" garantindo que sempre haja um caminho válido.
        private static string DB_NAME
        {
            get
            {
                // Tentativa 1: Diretório base onde o .exe está instalado (Padrão mais seguro)
                string? path = AppDomain.CurrentDomain.BaseDirectory;

                // Tentativa 2: Se falhar, tenta o diretório atual de execução
                if (string.IsNullOrEmpty(path))
                {
                    path = Directory.GetCurrentDirectory();
                }

                // Tentativa 3: Se tudo falhar (muito raro), usa ponto "." (pasta corrente relativa)
                // Isso evita que o Path.Combine receba null e trave o programa
                if (string.IsNullOrEmpty(path))
                {
                    path = ".";
                }

                return Path.Combine(path, "FTO.db");
            }
        }

        private static string ConnectionString => $"Data Source={DB_NAME};Version=3;Pooling=False;Cache Size=5000;Page Size=4096;Journal Mode=WAL;";

        public static void InitTables()
        {
            // Tenta conectar. Se o arquivo não existir, o SQLite cria um vazio automaticamente aqui.
            // Se já existir (caso do seu cliente), ele apenas abre.
            using (var conn = GetConnection())
            {
                // Otimizações de desempenho (WAL Mode)
                using (var cmdPragma = new SQLiteCommand(conn))
                {
                    cmdPragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA temp_store=MEMORY;";
                    cmdPragma.ExecuteNonQuery();
                }

                // Criação das tabelas APENAS SE ELAS NÃO EXISTIREM
                // Isso garante que os dados antigos do seu cliente NÃO sejam apagados.
                using (var cmd = new SQLiteCommand(conn))
                {
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS Users (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            User TEXT NOT NULL UNIQUE,
                            Senha TEXT NOT NULL
                        );
                        CREATE TABLE IF NOT EXISTS Clientes (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            Nome TEXT NOT NULL,
                            Contato TEXT,
                            Cpf_Cnpj TEXT
                        );
                        CREATE TABLE IF NOT EXISTS Vendas (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            Cliente TEXT,
                            Contato TEXT,
                            Data TEXT,
                            Gastos DECIMAL(10,2),
                            Venda DECIMAL(10,2),
                            TipoServico TEXT,
                            FormaPag TEXT,
                            Pago TEXT,
                            CPF_CNPJ TEXT
                        );
                        -- Cria índices para deixar a busca rápida
                        CREATE INDEX IF NOT EXISTS idx_vendas_data ON Vendas(Data);
                        CREATE INDEX IF NOT EXISTS idx_vendas_cliente ON Vendas(Cliente);
                        CREATE INDEX IF NOT EXISTS idx_clientes_nome ON Clientes(Nome);
                    ";
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public static SQLiteConnection GetConnection()
        {
            var conn = new SQLiteConnection(ConnectionString);
            conn.Open();
            return conn;
        }

        public static void ExecuteNonQuery(string sql, Dictionary<string, object> parameters)
        {
            using (var conn = GetConnection())
            using (var cmd = new SQLiteCommand(sql, conn))
            {
                if (parameters != null)
                {
                    foreach (var param in parameters)
                        cmd.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
                }
                cmd.ExecuteNonQuery();
            }
        }
    }
}