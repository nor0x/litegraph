namespace LiteGraph.GraphRepositories
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Data.Common;
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;

    /// <summary>
    /// Loads query results into a <see cref="DataTable"/>.
    /// <see cref="DataTable.Load(IDataReader)"/> is annotated as unsafe for trimming because a table may contain expression
    /// columns, whose evaluation can reach members of arbitrary types through reflection. LiteGraph result tables are
    /// created empty and filled only from data readers, so they never have expression columns. Native AOT runs of the
    /// SQLite and PostgreSQL repositories (Test.Aot) verify this.
    /// Keeping <see cref="DataTable.Load(IDataReader)"/> (rather than copying rows by hand) preserves its exact semantics:
    /// key and unique constraints from the reader's schema, merging of rows with the same key, and advancing the reader to
    /// the next result set.
    /// Thread safety: stateless; the table and reader are not thread-safe and must not be shared during the call.
    /// </summary>
    internal static class DataTableLoader
    {
        #region Public-Members

        #endregion

        #region Private-Members

        private const string _Justification =
            "LiteGraph result tables never define DataColumn expressions, the only feature behind the annotation; "
            + "verified under Native AOT by Test.Aot.";

        #endregion

        #region Constructors-and-Factories

        #endregion

        #region Public-Methods

        /// <summary>
        /// Load the reader's current result set into the table, then advance the reader as
        /// <see cref="DataTable.Load(IDataReader)"/> does.
        /// </summary>
        /// <param name="table">Table without expression columns.</param>
        /// <param name="reader">Data reader.</param>
        [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode", Justification = _Justification)]
        internal static void Load(DataTable table, IDataReader reader)
        {
            table.Load(reader);
        }

        /// <summary>
        /// Load the reader's current result set into the table without calling <see cref="DbDataReader.GetSchemaTable"/>,
        /// then advance the reader as <see cref="DataTable.Load(IDataReader)"/> does.
        /// For SQLite: Microsoft.Data.Sqlite answers GetSchemaTable with a "SELECT typeof(column) ... GROUP BY" scan over
        /// the whole source table for every result column, so <see cref="DataTable.Load(IDataReader)"/> turns each
        /// millisecond query into seconds, growing with the table.
        /// Column names are made unique exactly as <see cref="DataTable.Load(IDataReader)"/> does, so joined results keep the
        /// first table's names (guid, guid1, ...). No key constraints are applied: single-table results are keyed by a unique
        /// guid, so no rows would merge, and DataTable.Load applies no key to joined results, whose columns span tables.
        /// </summary>
        /// <param name="table">Empty table.</param>
        /// <param name="reader">Data reader.</param>
        internal static void LoadWithoutSchemaTable(DataTable table, DbDataReader reader)
        {
            if (reader.FieldCount > 0)
            {
                string[] names = new string[reader.FieldCount];
                for (int i = 0; i < names.Length; i++) names[i] = reader.GetName(i);
                MakeColumnNamesUnique(names);

                for (int i = 0; i < names.Length; i++)
                {
                    table.Columns.Add(new DataColumn(names[i], reader.GetFieldType(i)));
                }

                object[] values = new object[names.Length];
                table.BeginLoadData();

                try
                {
                    while (reader.Read())
                    {
                        reader.GetValues(values);
                        table.LoadDataRow(values, true);
                    }
                }
                finally
                {
                    table.EndLoadData();
                }
            }

            if (!reader.IsClosed && !reader.NextResult()) reader.Close();
        }

        #endregion

        #region Private-Methods

        private static void MakeColumnNamesUnique(string[] names)
        {
            Dictionary<string, int> lastIndex = new Dictionary<string, int>(names.Length);
            int startIndex = names.Length;

            for (int i = names.Length - 1; i >= 0; i--)
            {
                string name = names[i];
                if (!String.IsNullOrEmpty(name))
                {
                    string key = name.ToLowerInvariant();
                    if (lastIndex.TryGetValue(key, out int index)) startIndex = Math.Min(startIndex, index);
                    lastIndex[key] = i;
                }
                else
                {
                    names[i] = String.Empty;
                    startIndex = i;
                }
            }

            int uniqueIndex = 1;
            for (int i = startIndex; i < names.Length; i++)
            {
                if (names[i].Length == 0)
                {
                    uniqueIndex = AssignUniqueColumnName(lastIndex, names, i, "Column", uniqueIndex);
                }
                else if (lastIndex[names[i].ToLowerInvariant()] != i)
                {
                    AssignUniqueColumnName(lastIndex, names, i, names[i], 1);
                }
            }
        }

        private static int AssignUniqueColumnName(Dictionary<string, int> lastIndex, string[] names, int position, string baseName, int uniqueIndex)
        {
            while (true)
            {
                string candidate = baseName + uniqueIndex.ToString(CultureInfo.InvariantCulture);
                string key = candidate.ToLowerInvariant();
                if (!lastIndex.ContainsKey(key))
                {
                    names[position] = candidate;
                    lastIndex.Add(key, position);
                    return uniqueIndex;
                }

                uniqueIndex++;
            }
        }

        #endregion
    }
}
