using Microsoft.Data.Sqlite;

if (args.Length < 2)
{
    Console.WriteLine("Usage: SqlRunner <db-path> <query-file-or-inline-sql>");
    return 1;
}

var dbPath = args[0];
var sqlArg = args[1];
string sql = File.Exists(sqlArg) ? File.ReadAllText(sqlArg) : sqlArg;

using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
conn.Open();

// Allow multiple statements separated by ';GO' on a line of its own
foreach (var statement in SplitStatements(sql))
{
    if (string.IsNullOrWhiteSpace(statement)) continue;
    Console.WriteLine("--- SQL ---");
    Console.WriteLine(statement.Trim());
    Console.WriteLine("--- RESULT ---");
    using var cmd = conn.CreateCommand();
    cmd.CommandText = statement;
    using var reader = cmd.ExecuteReader();
    var cols = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
    Console.WriteLine(string.Join(" | ", cols));
    Console.WriteLine(string.Join(" | ", cols.Select(c => new string('-', c.Length))));
    int rows = 0;
    while (reader.Read())
    {
        var values = new string[reader.FieldCount];
        for (int i = 0; i < reader.FieldCount; i++)
            values[i] = reader.IsDBNull(i) ? "NULL" : reader.GetValue(i)?.ToString() ?? "NULL";
        Console.WriteLine(string.Join(" | ", values));
        rows++;
    }
    Console.WriteLine($"({rows} row{(rows == 1 ? "" : "s")})");
    Console.WriteLine();
}
return 0;

static IEnumerable<string> SplitStatements(string sql)
{
    var lines = sql.Replace("\r\n", "\n").Split('\n');
    var buf = new System.Text.StringBuilder();
    foreach (var line in lines)
    {
        if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
        {
            yield return buf.ToString();
            buf.Clear();
        }
        else
        {
            buf.AppendLine(line);
        }
    }
    if (buf.Length > 0) yield return buf.ToString();
}
