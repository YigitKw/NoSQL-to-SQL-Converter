using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Proje3
{
    public class DatabaseManager : IDisposable
    {
        private readonly SqliteConnection _sqliteConnection;
        private SqliteTransaction? _activeTransaction;

        // Tablo bazlı prepared command önbelleği
        private readonly Dictionary<string, SqliteCommand> _preparedInsertCommands = new();

        public DatabaseManager(string databaseFilePath)
        {
            _sqliteConnection = new SqliteConnection($"Data Source={databaseFilePath}");
            _sqliteConnection.Open();

            // Toplu eklemede disk I/O darboğazını önlemek için WAL + NORMAL
            ExecuteNonQuery("PRAGMA foreign_keys = ON;");
            ExecuteNonQuery("PRAGMA journal_mode = WAL;");
            ExecuteNonQuery("PRAGMA synchronous = NORMAL;");
        }

        public string CreateTable(TableSchema tableSchema)
        {
            var sqlBuilder = new StringBuilder();
            bool hasDeclaredColumns = tableSchema.Columns.Count > 0;
            sqlBuilder.AppendLine($"CREATE TABLE IF NOT EXISTS \"{tableSchema.TableName}\" (");

            if (tableSchema.ParentTable != null)
            {
                sqlBuilder.AppendLine($"  \"_id\" INTEGER PRIMARY KEY AUTOINCREMENT,");
                sqlBuilder.AppendLine($"  \"{tableSchema.ParentTable}__id\" INTEGER REFERENCES \"{tableSchema.ParentTable}\"(\"_id\"){(hasDeclaredColumns ? "," : "")}");
            }
            else
            {
                sqlBuilder.AppendLine($"  \"_id\" INTEGER PRIMARY KEY AUTOINCREMENT{(hasDeclaredColumns ? "," : "")}");
            }

            for (int columnIndex = 0; columnIndex < tableSchema.Columns.Count; columnIndex++)
            {
                var columnDefinition = tableSchema.Columns[columnIndex];
                bool isLastColumn = (columnIndex == tableSchema.Columns.Count - 1);
                sqlBuilder.AppendLine($"  \"{columnDefinition.Name}\" {columnDefinition.SqlType}{(isLastColumn ? "" : ",")}");
            }
            sqlBuilder.Append(");");

            string createTableSql = sqlBuilder.ToString();
            ExecuteNonQuery(createTableSql);
            return createTableSql;
        }

        public (long insertedId, string sql) InsertRecord(FlatRecord targetRecord, TableSchema tableSchema, long? parentRecordId)
        {
            string tableCacheKey = tableSchema.TableName;

            if (!_preparedInsertCommands.TryGetValue(tableCacheKey, out var preparedInsertCommand))
            {
                preparedInsertCommand = BuildPreparedInsert(tableSchema);
                _preparedInsertCommands[tableCacheKey] = preparedInsertCommand;
            }

            if (parentRecordId.HasValue && tableSchema.ParentTable != null)
                preparedInsertCommand.Parameters[$"@p_{tableSchema.ParentTable}__id"].Value = parentRecordId.Value;
            else if (tableSchema.ParentTable != null)
                preparedInsertCommand.Parameters[$"@p_{tableSchema.ParentTable}__id"].Value = DBNull.Value;

            foreach (var columnDefinition in tableSchema.Columns)
            {
                if (targetRecord.Fields.TryGetValue(columnDefinition.Name, out var fieldValue))
                    preparedInsertCommand.Parameters[$"@p_{columnDefinition.Name}"].Value = fieldValue ?? DBNull.Value;
                else
                    preparedInsertCommand.Parameters[$"@p_{columnDefinition.Name}"].Value = DBNull.Value;
            }

            // Açık transaction varsa komuta bağlanmak zorunda
            if (_activeTransaction != null)
                preparedInsertCommand.Transaction = _activeTransaction;

            preparedInsertCommand.ExecuteNonQuery();

            using var lastRowIdCommand = _sqliteConnection.CreateCommand();
            lastRowIdCommand.CommandText = "SELECT last_insert_rowid();";
            if (_activeTransaction != null)
                lastRowIdCommand.Transaction = _activeTransaction;

            long generatedRowId = (long)(lastRowIdCommand.ExecuteScalar() ?? 0);
            return (generatedRowId, preparedInsertCommand.CommandText);
        }

        private SqliteCommand BuildPreparedInsert(TableSchema tableSchema)
        {
            var columnNames = new List<string>();
            var parameterPlaceholders = new List<string>();

            if (tableSchema.ParentTable != null)
            {
                columnNames.Add($"\"{tableSchema.ParentTable}__id\"");
                parameterPlaceholders.Add($"@p_{tableSchema.ParentTable}__id");
            }

            foreach (var columnDefinition in tableSchema.Columns)
            {
                columnNames.Add($"\"{columnDefinition.Name}\"");
                parameterPlaceholders.Add($"@p_{columnDefinition.Name}");
            }

            string insertSqlTemplate = columnNames.Count == 0
                ? $"INSERT INTO \"{tableSchema.TableName}\" DEFAULT VALUES;"
                : $"INSERT INTO \"{tableSchema.TableName}\" ({string.Join(", ", columnNames)}) VALUES ({string.Join(", ", parameterPlaceholders)});";

            var insertCommand = _sqliteConnection.CreateCommand();
            insertCommand.CommandText = insertSqlTemplate;

            if (tableSchema.ParentTable != null)
                insertCommand.Parameters.Add(new SqliteParameter($"@p_{tableSchema.ParentTable}__id", DBNull.Value));

            foreach (var columnDefinition in tableSchema.Columns)
                insertCommand.Parameters.Add(new SqliteParameter($"@p_{columnDefinition.Name}", DBNull.Value));

            insertCommand.Prepare();
            return insertCommand;
        }

        public void BeginTransaction()
        {
            _activeTransaction = _sqliteConnection.BeginTransaction();
        }

        public void CommitTransaction()
        {
            _activeTransaction?.Commit();
            _activeTransaction?.Dispose();
            _activeTransaction = null;
        }

        public void RollbackTransaction()
        {
            _activeTransaction?.Rollback();
            _activeTransaction?.Dispose();
            _activeTransaction = null;
        }

        public DataTable ExecuteQuery(string selectSqlQuery)
        {
            using var queryCommand = _sqliteConnection.CreateCommand();
            queryCommand.CommandText = selectSqlQuery;
            using var sqliteDataReader = queryCommand.ExecuteReader();
            var resultDataTable = new DataTable();
            resultDataTable.Load(sqliteDataReader);
            return resultDataTable;
        }

        public List<string> GetTableNames()
        {
            var tableNames = new List<string>();
            var metadataTable = ExecuteQuery("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;");
            foreach (DataRow dataRow in metadataTable.Rows)
                tableNames.Add(dataRow[0]?.ToString() ?? string.Empty);
            return tableNames;
        }

        public List<string> DropAllTables()
        {
            var droppedTableStatements = new List<string>();
            ExecuteNonQuery("PRAGMA foreign_keys = OFF;");
            var existingTableNames = GetTableNames();
            foreach (var tableName in existingTableNames)
            {
                if (tableName.StartsWith("sqlite_")) continue;
                string dropTableSql = $"DROP TABLE IF EXISTS \"{tableName}\";";
                ExecuteNonQuery(dropTableSql);
                droppedTableStatements.Add(dropTableSql);
            }

            foreach (var cachedCommand in _preparedInsertCommands.Values)
                cachedCommand.Dispose();
            _preparedInsertCommands.Clear();

            ExecuteNonQuery("PRAGMA foreign_keys = ON;");
            return droppedTableStatements;
        }

        public string ExecuteNonQuery(string nonQuerySql)
        {
            using var nonQueryCommand = _sqliteConnection.CreateCommand();
            nonQueryCommand.CommandText = nonQuerySql;
            nonQueryCommand.ExecuteNonQuery();
            return nonQuerySql;
        }

        public void Dispose()
        {
            foreach (var cachedCommand in _preparedInsertCommands.Values)
                cachedCommand.Dispose();
            _preparedInsertCommands.Clear();

            _sqliteConnection?.Close();
            _sqliteConnection?.Dispose();
        }
    }
}
