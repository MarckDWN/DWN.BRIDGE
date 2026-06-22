using System;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using MySql.Data.MySqlClient;
using Npgsql;
using System.Data.Common;
using System.Data.OleDb;
using System.Data.Odbc;
using Microsoft.Data.Sqlite;
using Oracle.ManagedDataAccess.Client;

namespace AIBridge.Services
{
    public class SqlExecutionService
    {
        public async Task<DataTable> ExecuteQueryAsync(string provider, string connectionString, string query)
        {
            using var connection = CreateConnection(provider, connectionString);
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = query;

            using var reader = await command.ExecuteReaderAsync();
            var dataTable = new DataTable();
            dataTable.Load(reader);
            
            return dataTable;
        }

        public async Task<string> GetSchemaAsync(string provider, string connectionString)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Schema del Database:");

            if (provider.ToLower() is "excel" or "oledb" or "odbc" or "db2" or "as400" or "sqlite" or "oracle")
            {
                using var connection = CreateConnection(provider, connectionString);
                await connection.OpenAsync();
                var schemaDt = connection.GetSchema("Columns");
                foreach (DataRow row in schemaDt.Rows)
                {
                    string tableName = row.Table.Columns.Contains("TABLE_NAME") ? row["TABLE_NAME"]?.ToString() ?? "" : "";
                    string columnName = row.Table.Columns.Contains("COLUMN_NAME") ? row["COLUMN_NAME"]?.ToString() ?? "" : "";
                    
                    string dataType = "";
                    if (row.Table.Columns.Contains("DATA_TYPE")) dataType = row["DATA_TYPE"]?.ToString() ?? "";
                    else if (row.Table.Columns.Contains("DATATYPE")) dataType = row["DATATYPE"]?.ToString() ?? "";
                    else if (row.Table.Columns.Contains("TYPE_NAME")) dataType = row["TYPE_NAME"]?.ToString() ?? "";

                    sb.AppendLine($"- Tabella: {tableName} | Colonna: {columnName} | Tipo: {dataType}");
                }
                return sb.ToString();
            }

            // Metodo base per estrarre lo schema dal DB
            string query = "";
            switch (provider.ToLower())
            {
                case "sqlserver":
                    query = "SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS ORDER BY TABLE_NAME, ORDINAL_POSITION";
                    break;
                case "mysql":
                    query = "SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() ORDER BY TABLE_NAME, ORDINAL_POSITION";
                    break;
                case "postgresql":
                    query = "SELECT table_name, column_name, data_type FROM information_schema.columns WHERE table_schema = 'public' ORDER BY table_name, ordinal_position";
                    break;
                default:
                    throw new NotSupportedException($"Provider {provider} non supportato.");
            }
            
            var dt = await ExecuteQueryAsync(provider, connectionString, query);
            
            foreach (DataRow row in dt.Rows)
            {
                sb.AppendLine($"- Tabella: {row[0]} | Colonna: {row[1]} | Tipo: {row[2]}");
            }
            return sb.ToString();
        }

        private DbConnection CreateConnection(string provider, string connectionString)
        {
            return provider.ToLower() switch
            {
                "sqlserver" => new SqlConnection(connectionString),
                "mysql" => new MySqlConnection(connectionString),
                "postgresql" => new NpgsqlConnection(connectionString),
                "excel" or "oledb" => new OleDbConnection(connectionString),
                "odbc" or "db2" or "as400" => new OdbcConnection(connectionString),
                "sqlite" => new SqliteConnection(connectionString),
                "oracle" => new OracleConnection(connectionString),
                _ => throw new ArgumentException($"Provider DB '{provider}' non supportato.", nameof(provider))
            };
        }
    }
}
