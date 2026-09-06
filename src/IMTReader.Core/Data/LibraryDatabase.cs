using System.Globalization;
using IMTReader.Core.Models;
using Microsoft.Data.Sqlite;

namespace IMTReader.Core.Data;

/// <summary>
/// 文献库数据库（SQLite）。单进程使用，所有操作以锁串行化，可在 OCR 后台线程与 UI 线程之间安全共享。
/// </summary>
public sealed class LibraryDatabase : IDisposable
{
    private readonly object _gate = new();
    private readonly SqliteConnection _conn;

    public string Path { get; }

    public LibraryDatabase(string path)
    {
        Path = path;
        var dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());
        _conn.Open();
        Exec("PRAGMA journal_mode=WAL;");
        CreateSchema();
    }

    private void CreateSchema()
    {
        Exec(@"
CREATE TABLE IF NOT EXISTS documents(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    title TEXT NOT NULL,
    kind INTEGER NOT NULL,
    source_path TEXT NOT NULL,
    page_count INTEGER NOT NULL,
    added_at TEXT NOT NULL,
    last_opened_at TEXT,
    last_page INTEGER NOT NULL DEFAULT 0);
CREATE TABLE IF NOT EXISTS pages(
    document_id INTEGER NOT NULL,
    page_index INTEGER NOT NULL,
    status INTEGER NOT NULL DEFAULT 0,
    ocr_at TEXT,
    engine TEXT,
    PRIMARY KEY(document_id, page_index));
CREATE TABLE IF NOT EXISTS blocks(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    document_id INTEGER NOT NULL,
    page_index INTEGER NOT NULL,
    ord INTEGER NOT NULL,
    x REAL NOT NULL DEFAULT 0, y REAL NOT NULL DEFAULT 0,
    w REAL NOT NULL DEFAULT 0, h REAL NOT NULL DEFAULT 0,
    vertical INTEGER NOT NULL DEFAULT 0,
    text TEXT NOT NULL,
    score REAL NOT NULL DEFAULT 0);
CREATE INDEX IF NOT EXISTS ix_blocks_doc_page ON blocks(document_id, page_index, ord);
CREATE TABLE IF NOT EXISTS highlights(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    document_id INTEGER NOT NULL,
    page_index INTEGER NOT NULL,
    block_id INTEGER NOT NULL,
    start INTEGER NOT NULL,
    length INTEGER NOT NULL,
    color TEXT NOT NULL,
    excerpt TEXT NOT NULL,
    created_at TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS ix_highlights_doc ON highlights(document_id, page_index);
CREATE INDEX IF NOT EXISTS ix_highlights_block ON highlights(block_id);
CREATE TABLE IF NOT EXISTS bookmarks(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    document_id INTEGER NOT NULL,
    page_index INTEGER NOT NULL,
    title TEXT NOT NULL,
    created_at TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS notes(
    document_id INTEGER PRIMARY KEY,
    content TEXT NOT NULL,
    updated_at TEXT NOT NULL);
");
    }

    // ------------------------------------------------------------------ helpers

    private static string D(DateTime dt) => dt.ToString("o", CultureInfo.InvariantCulture);

    private static DateTime? PD(object? v) =>
        v is string s && s.Length > 0
            ? DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            : null;

    private SqliteCommand Cmd(string sql, params (string name, object? value)[] args)
    {
        var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    private void Exec(string sql, params (string name, object? value)[] args)
    {
        lock (_gate)
        {
            using var cmd = Cmd(sql, args);
            cmd.ExecuteNonQuery();
        }
    }

    private long ExecInsert(string sql, params (string name, object? value)[] args)
    {
        lock (_gate)
        {
            using var cmd = Cmd(sql + "; SELECT last_insert_rowid();", args);
            return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
        }
    }

    private List<T> Query<T>(string sql, Func<SqliteDataReader, T> map, params (string name, object? value)[] args)
    {
        lock (_gate)
        {
            using var cmd = Cmd(sql, args);
            using var r = cmd.ExecuteReader();
            var list = new List<T>();
            while (r.Read()) list.Add(map(r));
            return list;
        }
    }

    private T? Scalar<T>(string sql, params (string name, object? value)[] args)
    {
        lock (_gate)
        {
            using var cmd = Cmd(sql, args);
            var v = cmd.ExecuteScalar();
            if (v == null || v is DBNull) return default;
            return (T)Convert.ChangeType(v, typeof(T), CultureInfo.InvariantCulture);
        }
    }

    private void ExecTx(SqliteTransaction tx, string sql, params (string name, object? value)[] args)
    {
        using var c = _conn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = sql;
        foreach (var (name, value) in args) c.Parameters.AddWithValue(name, value ?? DBNull.Value);
        c.ExecuteNonQuery();
    }

    // ------------------------------------------------------------------ documents

    private const string DocSelect = @"
SELECT d.id, d.title, d.kind, d.source_path, d.page_count, d.added_at, d.last_opened_at, d.last_page,
       (SELECT COUNT(*) FROM pages p WHERE p.document_id = d.id AND p.status = 1) AS done_pages
FROM documents d";

    private static DocumentInfo MapDocument(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        Title = r.GetString(1),
        Kind = (SourceKind)r.GetInt32(2),
        SourcePath = r.GetString(3),
        PageCount = r.GetInt32(4),
        AddedAt = PD(r.GetValue(5)) ?? DateTime.Now,
        LastOpenedAt = PD(r.GetValue(6)),
        LastPage = r.GetInt32(7),
        OcrDonePages = r.GetInt32(8)
    };

    public long AddDocument(DocumentInfo doc)
    {
        doc.Id = ExecInsert(
            "INSERT INTO documents(title, kind, source_path, page_count, added_at, last_page) VALUES(@t, @k, @p, @n, @a, 0)",
            ("@t", doc.Title), ("@k", (int)doc.Kind), ("@p", doc.SourcePath), ("@n", doc.PageCount), ("@a", D(doc.AddedAt)));
        return doc.Id;
    }

    public List<DocumentInfo> GetDocuments() => Query(DocSelect + " ORDER BY d.added_at DESC", MapDocument);

    public List<DocumentInfo> GetRecentDocuments(int limit = 30) =>
        Query(DocSelect + " WHERE d.last_opened_at IS NOT NULL ORDER BY d.last_opened_at DESC LIMIT @l", MapDocument, ("@l", limit));

    public DocumentInfo? GetDocument(long id) =>
        Query(DocSelect + " WHERE d.id = @id", MapDocument, ("@id", id)).FirstOrDefault();

    public DocumentInfo? FindDocumentBySource(string sourcePath) =>
        Query(DocSelect + " WHERE d.source_path = @p", MapDocument, ("@p", sourcePath)).FirstOrDefault();

    public void RenameDocument(long id, string title) =>
        Exec("UPDATE documents SET title = @t WHERE id = @id", ("@t", title), ("@id", id));

    public void UpdateSourcePath(long id, string sourcePath) =>
        Exec("UPDATE documents SET source_path = @p WHERE id = @id", ("@p", sourcePath), ("@id", id));

    public void TouchDocument(long id, int lastPage) =>
        Exec("UPDATE documents SET last_opened_at = @a, last_page = @p WHERE id = @id",
            ("@a", D(DateTime.Now)), ("@p", lastPage), ("@id", id));

    public void DeleteDocument(long id)
    {
        lock (_gate)
        {
            using var tx = _conn.BeginTransaction();
            foreach (var table in new[] { "highlights", "bookmarks", "blocks", "pages", "notes" })
                ExecTx(tx, $"DELETE FROM {table} WHERE document_id = @id", ("@id", id));
            ExecTx(tx, "DELETE FROM documents WHERE id = @id", ("@id", id));
            tx.Commit();
        }
    }

    // ------------------------------------------------------------------ pages

    public Dictionary<int, PageInfo> GetPageStatuses(long documentId)
    {
        var list = Query("SELECT page_index, status, ocr_at, engine FROM pages WHERE document_id = @d",
            r => new PageInfo
            {
                DocumentId = documentId,
                PageIndex = r.GetInt32(0),
                Status = (OcrStatus)r.GetInt32(1),
                OcrAt = PD(r.GetValue(2)),
                Engine = r.IsDBNull(3) ? null : r.GetString(3)
            }, ("@d", documentId));
        var dict = new Dictionary<int, PageInfo>();
        foreach (var p in list) dict[p.PageIndex] = p;
        return dict;
    }

    public void SetPageStatus(long documentId, int pageIndex, OcrStatus status, string? engine) =>
        Exec(@"INSERT INTO pages(document_id, page_index, status, ocr_at, engine) VALUES(@d, @p, @s, @a, @e)
ON CONFLICT(document_id, page_index) DO UPDATE SET status = excluded.status, ocr_at = excluded.ocr_at, engine = excluded.engine",
            ("@d", documentId), ("@p", pageIndex), ("@s", (int)status), ("@a", D(DateTime.Now)), ("@e", engine));

    public int CountOcrDonePages(long documentId) =>
        Scalar<int>("SELECT COUNT(*) FROM pages WHERE document_id = @d AND status = 1", ("@d", documentId));

    // ------------------------------------------------------------------ blocks

    private const string BlockSelect = "SELECT id, document_id, page_index, ord, x, y, w, h, vertical, text, score FROM blocks";

    private static OcrBlock MapBlock(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        DocumentId = r.GetInt64(1),
        PageIndex = r.GetInt32(2),
        Order = r.GetInt32(3),
        X = r.GetDouble(4),
        Y = r.GetDouble(5),
        W = r.GetDouble(6),
        H = r.GetDouble(7),
        IsVertical = r.GetInt32(8) != 0,
        Text = r.GetString(9),
        Score = r.GetDouble(10)
    };

    public List<OcrBlock> GetPageBlocks(long documentId, int pageIndex) =>
        Query(BlockSelect + " WHERE document_id = @d AND page_index = @p ORDER BY ord", MapBlock, ("@d", documentId), ("@p", pageIndex));

    public List<OcrBlock> GetDocumentBlocks(long documentId) =>
        Query(BlockSelect + " WHERE document_id = @d ORDER BY page_index, ord", MapBlock, ("@d", documentId));

    public List<OcrBlock> GetAllBlocks() =>
        Query(BlockSelect + " ORDER BY document_id, page_index, ord", MapBlock);

    public OcrBlock? GetBlock(long id) =>
        Query(BlockSelect + " WHERE id = @id", MapBlock, ("@id", id)).FirstOrDefault();

    /// <summary>用新的识别结果替换整页文本块（同时清除该页的高亮）。</summary>
    public void ReplacePageBlocks(long documentId, int pageIndex, IEnumerable<OcrBlock> blocks)
    {
        lock (_gate)
        {
            using var tx = _conn.BeginTransaction();
            ExecTx(tx, "DELETE FROM highlights WHERE document_id = @d AND page_index = @p", ("@d", documentId), ("@p", pageIndex));
            ExecTx(tx, "DELETE FROM blocks WHERE document_id = @d AND page_index = @p", ("@d", documentId), ("@p", pageIndex));
            int ord = 0;
            foreach (var b in blocks) InsertBlockTx(tx, documentId, pageIndex, ord++, b);
            tx.Commit();
        }
    }

    /// <summary>在现有块之后追加文本块（用于框选识别）。</summary>
    public void AppendPageBlocks(long documentId, int pageIndex, IEnumerable<OcrBlock> blocks)
    {
        lock (_gate)
        {
            using var tx = _conn.BeginTransaction();
            int ord;
            using (var c = _conn.CreateCommand())
            {
                c.Transaction = tx;
                c.CommandText = "SELECT COALESCE(MAX(ord), -1) + 1 FROM blocks WHERE document_id = @d AND page_index = @p";
                c.Parameters.AddWithValue("@d", documentId);
                c.Parameters.AddWithValue("@p", pageIndex);
                ord = Convert.ToInt32(c.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
            foreach (var b in blocks) InsertBlockTx(tx, documentId, pageIndex, ord++, b);
            tx.Commit();
        }
    }

    private void InsertBlockTx(SqliteTransaction tx, long documentId, int pageIndex, int ord, OcrBlock b)
    {
        using var c = _conn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = "INSERT INTO blocks(document_id, page_index, ord, x, y, w, h, vertical, text, score) VALUES(@d, @p, @o, @x, @y, @w, @h, @v, @t, @s); SELECT last_insert_rowid();";
        c.Parameters.AddWithValue("@d", documentId);
        c.Parameters.AddWithValue("@p", pageIndex);
        c.Parameters.AddWithValue("@o", ord);
        c.Parameters.AddWithValue("@x", b.X);
        c.Parameters.AddWithValue("@y", b.Y);
        c.Parameters.AddWithValue("@w", b.W);
        c.Parameters.AddWithValue("@h", b.H);
        c.Parameters.AddWithValue("@v", b.IsVertical ? 1 : 0);
        c.Parameters.AddWithValue("@t", b.Text);
        c.Parameters.AddWithValue("@s", b.Score);
        b.Id = Convert.ToInt64(c.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
        b.DocumentId = documentId;
        b.PageIndex = pageIndex;
        b.Order = ord;
    }

    public void UpdateBlockText(long blockId, string text) =>
        Exec("UPDATE blocks SET text = @t WHERE id = @id", ("@t", text), ("@id", blockId));

    public void DeleteBlock(long blockId)
    {
        Exec("DELETE FROM highlights WHERE block_id = @id", ("@id", blockId));
        Exec("DELETE FROM blocks WHERE id = @id", ("@id", blockId));
    }

    public void DeletePageBlocks(long documentId, int pageIndex)
    {
        Exec("DELETE FROM highlights WHERE document_id = @d AND page_index = @p", ("@d", documentId), ("@p", pageIndex));
        Exec("DELETE FROM blocks WHERE document_id = @d AND page_index = @p", ("@d", documentId), ("@p", pageIndex));
        Exec("DELETE FROM pages WHERE document_id = @d AND page_index = @p", ("@d", documentId), ("@p", pageIndex));
    }

    // ------------------------------------------------------------------ highlights

    private const string HlSelect = "SELECT id, document_id, page_index, block_id, start, length, color, excerpt, created_at FROM highlights";

    private static Highlight MapHighlight(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        DocumentId = r.GetInt64(1),
        PageIndex = r.GetInt32(2),
        BlockId = r.GetInt64(3),
        Start = r.GetInt32(4),
        Length = r.GetInt32(5),
        Color = r.GetString(6),
        Excerpt = r.GetString(7),
        CreatedAt = PD(r.GetValue(8)) ?? DateTime.Now
    };

    public List<Highlight> GetDocumentHighlights(long documentId) =>
        Query(HlSelect + " WHERE document_id = @d ORDER BY page_index, block_id, start", MapHighlight, ("@d", documentId));

    public List<Highlight> GetPageHighlights(long documentId, int pageIndex) =>
        Query(HlSelect + " WHERE document_id = @d AND page_index = @p ORDER BY block_id, start", MapHighlight, ("@d", documentId), ("@p", pageIndex));

    /// <summary>用给定集合替换某文本块的全部高亮。</summary>
    public void ReplaceBlockHighlights(long documentId, int pageIndex, long blockId, IEnumerable<Highlight> highlights)
    {
        lock (_gate)
        {
            using var tx = _conn.BeginTransaction();
            ExecTx(tx, "DELETE FROM highlights WHERE block_id = @b", ("@b", blockId));
            foreach (var h in highlights)
            {
                ExecTx(tx, "INSERT INTO highlights(document_id, page_index, block_id, start, length, color, excerpt, created_at) VALUES(@d, @p, @b, @s, @l, @c, @e, @a)",
                    ("@d", documentId), ("@p", pageIndex), ("@b", blockId), ("@s", h.Start), ("@l", h.Length),
                    ("@c", h.Color), ("@e", h.Excerpt), ("@a", D(h.CreatedAt)));
            }
            tx.Commit();
        }
    }

    public void DeleteHighlight(long id) => Exec("DELETE FROM highlights WHERE id = @id", ("@id", id));

    public void ClearPageHighlights(long documentId, int pageIndex) =>
        Exec("DELETE FROM highlights WHERE document_id = @d AND page_index = @p", ("@d", documentId), ("@p", pageIndex));

    // ------------------------------------------------------------------ bookmarks

    public List<Bookmark> GetBookmarks(long documentId) =>
        Query("SELECT id, document_id, page_index, title, created_at FROM bookmarks WHERE document_id = @d ORDER BY page_index",
            r => new Bookmark
            {
                Id = r.GetInt64(0),
                DocumentId = r.GetInt64(1),
                PageIndex = r.GetInt32(2),
                Title = r.GetString(3),
                CreatedAt = PD(r.GetValue(4)) ?? DateTime.Now
            }, ("@d", documentId));

    public long AddBookmark(Bookmark b) => b.Id = ExecInsert(
        "INSERT INTO bookmarks(document_id, page_index, title, created_at) VALUES(@d, @p, @t, @a)",
        ("@d", b.DocumentId), ("@p", b.PageIndex), ("@t", b.Title), ("@a", D(b.CreatedAt)));

    public void DeleteBookmark(long id) => Exec("DELETE FROM bookmarks WHERE id = @id", ("@id", id));

    // ------------------------------------------------------------------ notes

    public string GetNotes(long documentId) =>
        Scalar<string>("SELECT content FROM notes WHERE document_id = @d", ("@d", documentId)) ?? string.Empty;

    public void SaveNotes(long documentId, string content) =>
        Exec(@"INSERT INTO notes(document_id, content, updated_at) VALUES(@d, @c, @a)
ON CONFLICT(document_id) DO UPDATE SET content = excluded.content, updated_at = excluded.updated_at",
            ("@d", documentId), ("@c", content), ("@a", D(DateTime.Now)));

    public void Dispose()
    {
        lock (_gate)
        {
            _conn.Dispose();
        }
    }
}
