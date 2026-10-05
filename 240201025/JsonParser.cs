using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Proje3
{
    public class TableSchema
    {
        public string TableName { get; set; } = string.Empty;
        public string? ParentTable { get; set; }
        public List<ColumnDef> Columns { get; set; } = new List<ColumnDef>();
    }

    public class ColumnDef
    {
        public string Name { get; set; } = string.Empty;
        public string SqlType { get; set; } = string.Empty;
    }

    public class FlatRecord
    {
        public string TableName { get; set; } = string.Empty;
        public Dictionary<string, object> Fields { get; set; } = new Dictionary<string, object>();
        public List<FlatRecord> Children { get; set; } = new List<FlatRecord>();
    }

    public static class JsonParser
    {
        public static (List<TableSchema> schemas, List<FlatRecord> records) Parse(string filePath)
        {
            string jsonContent = File.ReadAllText(filePath);
            JToken rootToken = JToken.Parse(jsonContent);
            string rootTableName = SanitizeIdentifier(Path.GetFileNameWithoutExtension(filePath));

            // 1. Geçiş: Şemaları ve kolon tiplerini baştan sona tara
            var schemaRegistry = new Dictionary<string, TableSchema>();
            WalkForSchemas(rootToken, rootTableName, null, schemaRegistry);

            // 2. Geçiş: Kesinleşen şemaya göre kayıtları üret
            var extractedRecords = new List<FlatRecord>();
            if (rootToken is JArray jsonArray)
            {
                foreach (var arrayElement in jsonArray)
                {
                    if (arrayElement is JObject jsonObjectElement)
                    {
                        var record = ProcessObject(jsonObjectElement, rootTableName, null, schemaRegistry);
                        extractedRecords.Add(record);
                    }
                    else
                    {
                        var primitiveRecord = new FlatRecord { TableName = rootTableName };
                        primitiveRecord.Fields["value"] = GetValue(arrayElement);
                        extractedRecords.Add(primitiveRecord);
                    }
                }
            }
            else if (rootToken is JObject rootJsonObject)
            {
                var record = ProcessObject(rootJsonObject, rootTableName, null, schemaRegistry);
                extractedRecords.Add(record);
            }
            else if (rootToken is JValue primitiveValue)
            {
                var primitiveRecord = new FlatRecord { TableName = rootTableName };
                primitiveRecord.Fields["value"] = GetValue(primitiveValue);
                extractedRecords.Add(primitiveRecord);
            }

            var discoveredSchemas = new List<TableSchema>(schemaRegistry.Values);
            FinalizeSchemas(discoveredSchemas);
            return (discoveredSchemas, extractedRecords);
        }

        private static void WalkForSchemas(
            JToken currentToken,
            string targetTableName,
            string? parentTableName,
            Dictionary<string, TableSchema> schemaRegistry)
        {
            if (currentToken is JArray jsonArray)
            {
                foreach (var arrayElement in jsonArray)
                {
                    if (arrayElement is JObject childJsonObject)
                    {
                        WalkForSchemas(childJsonObject, targetTableName, parentTableName, schemaRegistry);
                    }
                    else
                    {
                        var childSchema = GetOrCreateSchema(schemaRegistry, targetTableName, parentTableName);
                        EnsureColumn(childSchema, "value", GetSqlType(arrayElement.Type));
                    }
                }
                // Boş dizi gelse bile tablonun oluşması için
                GetOrCreateSchema(schemaRegistry, targetTableName, parentTableName);
            }
            else if (currentToken is JObject jsonObject)
            {
                var currentSchema = GetOrCreateSchema(schemaRegistry, targetTableName, parentTableName);
                foreach (var jsonProperty in jsonObject.Properties())
                {
                    string normalizedPropertyName = SanitizeIdentifier(jsonProperty.Name);
                    JToken propertyValue = jsonProperty.Value;

                    if (propertyValue is JObject nestedJsonObject)
                    {
                        WalkFlattenForSchemas(nestedJsonObject, normalizedPropertyName, targetTableName, currentSchema, schemaRegistry);
                    }
                    else if (propertyValue is JArray nestedJsonArray)
                    {
                        string childTableName = $"{targetTableName}_{normalizedPropertyName}";
                        WalkForSchemas(nestedJsonArray, childTableName, targetTableName, schemaRegistry);
                    }
                    else
                    {
                        EnsureColumn(currentSchema, normalizedPropertyName, GetSqlType(propertyValue.Type));
                    }
                }
            }
            else if (currentToken is JValue primitiveValue)
            {
                var primitiveSchema = GetOrCreateSchema(schemaRegistry, targetTableName, parentTableName);
                EnsureColumn(primitiveSchema, "value", GetSqlType(primitiveValue.Type));
            }
        }

        private static void WalkFlattenForSchemas(
            JObject nestedObject,
            string columnPrefix,
            string targetTableName,
            TableSchema targetSchema,
            Dictionary<string, TableSchema> schemaRegistry)
        {
            foreach (var jsonProperty in nestedObject.Properties())
            {
                string normalizedPropertyName = SanitizeIdentifier(jsonProperty.Name);
                string flattenedColumnName = $"{columnPrefix}_{normalizedPropertyName}";
                JToken propertyValue = jsonProperty.Value;

                if (propertyValue is JObject deeperNestedObject)
                {
                    WalkFlattenForSchemas(deeperNestedObject, flattenedColumnName, targetTableName, targetSchema, schemaRegistry);
                }
                else if (propertyValue is JArray nestedJsonArray)
                {
                    string childTableName = $"{targetTableName}_{flattenedColumnName}";
                    WalkForSchemas(nestedJsonArray, childTableName, targetTableName, schemaRegistry);
                }
                else
                {
                    EnsureColumn(targetSchema, flattenedColumnName, GetSqlType(propertyValue.Type));
                }
            }
        }

        private static TableSchema GetOrCreateSchema(
            Dictionary<string, TableSchema> schemaRegistry,
            string targetTableName,
            string? parentTableName)
        {
            if (!schemaRegistry.TryGetValue(targetTableName, out var existingSchema))
            {
                existingSchema = new TableSchema { TableName = targetTableName, ParentTable = parentTableName };
                schemaRegistry[targetTableName] = existingSchema;
            }
            return existingSchema;
        }

        private static FlatRecord ProcessObject(
            JObject sourceObject,
            string targetTableName,
            string? parentTableName,
            Dictionary<string, TableSchema> schemaRegistry)
        {
            if (!schemaRegistry.TryGetValue(targetTableName, out var tableSchema))
            {
                tableSchema = new TableSchema { TableName = targetTableName, ParentTable = parentTableName };
                schemaRegistry[targetTableName] = tableSchema;
            }

            var currentRecord = new FlatRecord { TableName = targetTableName };

            foreach (var jsonProperty in sourceObject.Properties())
            {
                string normalizedPropertyName = SanitizeIdentifier(jsonProperty.Name);
                JToken propertyValue = jsonProperty.Value;

                if (propertyValue is JObject nestedJsonObject)
                {
                    FlattenObject(nestedJsonObject, normalizedPropertyName, targetTableName, tableSchema, currentRecord, schemaRegistry);
                }
                else if (propertyValue is JArray childJsonArray)
                {
                    string childTableName = $"{targetTableName}_{normalizedPropertyName}";
                    ProcessArray(childJsonArray, childTableName, targetTableName, currentRecord, schemaRegistry);
                }
                else
                {
                    string inferredSqlType = GetSqlType(propertyValue.Type);
                    EnsureColumn(tableSchema, normalizedPropertyName, inferredSqlType);
                    currentRecord.Fields[normalizedPropertyName] = GetValue(propertyValue);
                }
            }

            return currentRecord;
        }

        private static void FlattenObject(
            JObject sourceObject,
            string columnPrefix,
            string targetTableName,
            TableSchema targetSchema,
            FlatRecord currentRecord,
            Dictionary<string, TableSchema> schemaRegistry)
        {
            foreach (var jsonProperty in sourceObject.Properties())
            {
                string normalizedPropertyName = SanitizeIdentifier(jsonProperty.Name);
                string flattenedColumnName = $"{columnPrefix}_{normalizedPropertyName}";
                JToken propertyValue = jsonProperty.Value;

                if (propertyValue is JObject deeperNestedObject)
                {
                    FlattenObject(deeperNestedObject, flattenedColumnName, targetTableName, targetSchema, currentRecord, schemaRegistry);
                }
                else if (propertyValue is JArray childJsonArray)
                {
                    // Dizi ile karşılaşıldığında 1:N ilişkisi başlar, alt tablo açılır
                    string childTableName = $"{targetTableName}_{flattenedColumnName}";
                    ProcessArray(childJsonArray, childTableName, targetTableName, currentRecord, schemaRegistry);
                }
                else
                {
                    string inferredSqlType = GetSqlType(propertyValue.Type);
                    EnsureColumn(targetSchema, flattenedColumnName, inferredSqlType);
                    currentRecord.Fields[flattenedColumnName] = GetValue(propertyValue);
                }
            }
        }

        private static void ProcessArray(
            JArray sourceArray,
            string childTableName,
            string parentTableName,
            FlatRecord parentRecord,
            Dictionary<string, TableSchema> schemaRegistry)
        {
            foreach (var arrayElement in sourceArray)
            {
                if (arrayElement is JObject childJsonObject)
                {
                    var childRecord = ProcessObject(childJsonObject, childTableName, parentTableName, schemaRegistry);
                    parentRecord.Children.Add(childRecord);
                }
                else
                {
                    if (!schemaRegistry.TryGetValue(childTableName, out var childSchema))
                    {
                        childSchema = new TableSchema { TableName = childTableName, ParentTable = parentTableName };
                        schemaRegistry[childTableName] = childSchema;
                    }
                    EnsureColumn(childSchema, "value", GetSqlType(arrayElement.Type));
                    var primitiveChildRecord = new FlatRecord { TableName = childTableName };
                    primitiveChildRecord.Fields["value"] = GetValue(arrayElement);
                    parentRecord.Children.Add(primitiveChildRecord);
                }
            }
        }

        private static void EnsureColumn(TableSchema targetSchema, string columnName, string incomingSqlType)
        {
            var existingColumn = targetSchema.Columns.Find(column => column.Name == columnName);
            if (existingColumn == null)
            {
                targetSchema.Columns.Add(new ColumnDef { Name = columnName, SqlType = incomingSqlType });
            }
            else
            {
                existingColumn.SqlType = PromoteType(existingColumn.SqlType, incomingSqlType);
            }
        }

        private static string PromoteType(string currentSqlType, string incomingSqlType)
        {
            if (currentSqlType == incomingSqlType) return currentSqlType;

            // Null ile başlayan alan tipi sonradan gelen değere göre belirlenir
            if (currentSqlType == "UNKNOWN") return incomingSqlType;
            if (incomingSqlType == "UNKNOWN") return currentSqlType;

            // Tip çakışmalarında veri kaybını önlemek için TEXT seçilir
            if (currentSqlType == "TEXT" || incomingSqlType == "TEXT") return "TEXT";

            if ((currentSqlType == "INTEGER" && incomingSqlType == "REAL") ||
                (currentSqlType == "REAL" && incomingSqlType == "INTEGER"))
                return "REAL";

            return "TEXT";
        }

        private static string GetSqlType(JTokenType tokenType) => tokenType switch
        {
            JTokenType.Integer => "INTEGER",
            JTokenType.Float   => "REAL",
            JTokenType.Boolean => "INTEGER",
            JTokenType.Null    => "UNKNOWN", // Başlangıçta tipi kilitlememek için
            _                  => "TEXT"
        };

        public static void FinalizeSchemas(List<TableSchema> schemaList)
        {
            foreach (var tableSchema in schemaList)
            {
                foreach (var columnDefinition in tableSchema.Columns)
                {
                    if (columnDefinition.SqlType == "UNKNOWN")
                        columnDefinition.SqlType = "TEXT";
                }
            }
        }

        private static object GetValue(JToken token) => token.Type switch
        {
            JTokenType.Integer => token.Value<long>(),
            JTokenType.Float   => token.Value<double>(),
            JTokenType.Boolean => token.Value<bool>() ? 1 : 0,
            JTokenType.Null    => DBNull.Value,
            _                  => token.ToString()
        };

        private static string SanitizeIdentifier(string rawIdentifierName)
        {
            var sanitizedBuilder = new StringBuilder();
            foreach (char character in rawIdentifierName)
            {
                if (char.IsLetterOrDigit(character) || character == '_')
                    sanitizedBuilder.Append(character);
                else
                    sanitizedBuilder.Append('_');
            }

            string sanitizedIdentifier = sanitizedBuilder.ToString().Trim('_');
            if (sanitizedIdentifier.Length > 0 && char.IsDigit(sanitizedIdentifier[0]))
                sanitizedIdentifier = $"t_{sanitizedIdentifier}";

            return sanitizedIdentifier.Length == 0 ? "field" : sanitizedIdentifier.ToLower();
        }
    }
}
