using System.Text;
using System.Text.RegularExpressions;

namespace Warden.Domain.Watch;

/// <summary>
/// Tail de arquivo + regex -> callback de erro, sem tocar no projeto observado. Cobre rotação de log
/// e stacktrace multi-linha (agrupa linhas até a próxima entrada começar, ou até ficar em silêncio
/// por <see cref="IdleFlushAfter"/> — cobre o erro fatal que é a última coisa escrita antes do
/// processo morrer). Mirror de `file_error_watcher.py`.
/// </summary>
/// <remarks>
/// Python usa `stat.st_ino` pra distinguir "arquivo recriado com mesmo nome" (logrotate) de
/// "mesmo arquivo, só cresceu". .NET não expõe inode sem P/Invoke a uma struct `stat` cujo layout
/// varia por libc/arquitetura — em vez de arriscar isso, usamos <see cref="FileInfo.CreationTimeUtc"/>
/// como identidade do arquivo: no Linux (.NET usa `statx`/`STATX_BTIME` quando o filesystem suporta,
/// ext4/xfs/btrfs suportam) ela muda quando o arquivo é recriado, igual trocaria o inode. Truncamento
/// copytruncate (mesmo arquivo, size zerado) continua coberto pelo shrink-check abaixo, que não depende disso.
/// </remarks>
public sealed class FileErrorWatcher
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan IdleFlushAfter = TimeSpan.FromSeconds(2);
    private const int MaxEntryLines = 200;

    private readonly string _path;
    private readonly List<Regex> _patterns;
    private readonly Action<string> _onError;
    private readonly ManualResetEventSlim _stopSignal = new(false);

    private Thread? _thread;
    private DateTime? _identity;
    private long _pos;
    private List<string>? _pending;
    private DateTime _lastLineAt;

    public FileErrorWatcher(string path, IEnumerable<string> patterns, Action<string> onError)
    {
        _path = path;
        _patterns = patterns.Select(p => new Regex(p)).ToList();
        _onError = onError;
    }

    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true };
        _thread.Start();
    }

    public void Stop()
    {
        _stopSignal.Set();
        _thread?.Join(TimeSpan.FromSeconds(PollInterval.TotalSeconds * 2));
    }

    private void Run()
    {
        while (!_stopSignal.IsSet)
        {
            PollOnce();
            _stopSignal.Wait(PollInterval);
        }
    }

    private void PollOnce()
    {
        FileInfo info;
        DateTime identity;
        try
        {
            info = new FileInfo(_path);
            if (!info.Exists) return; // arquivo ainda não existe / sumiu momentaneamente durante rotação
            identity = info.CreationTimeUtc;
        }
        catch (IOException)
        {
            return;
        }

        if (_identity is null)
        {
            _identity = identity;
            _pos = info.Length; // começa no fim — não reprocessa histórico
            return;
        }

        if (identity != _identity || info.Length < _pos)
        {
            _identity = identity;
            _pos = 0;
        }

        if (info.Length == _pos)
        {
            if (_pending is not null && DateTime.UtcNow - _lastLineAt > IdleFlushAfter)
            {
                FlushPending();
            }
            return;
        }

        var toRead = (int)(info.Length - _pos);
        var buffer = new byte[toRead];
        using (var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            stream.Seek(_pos, SeekOrigin.Begin);
            stream.ReadExactly(buffer);
        }
        _pos = info.Length;

        var newText = Utf8ReplaceFallback.GetString(buffer);
        var lines = newText.Split('\n');
        // texto terminado em \n sempre gera uma última linha "" no Split — não é conteúdo real
        // (mesma semântica de `str.splitlines()` do Python, que não conta essa linha vazia final).
        var lineCount = newText.EndsWith('\n') ? lines.Length - 1 : lines.Length;
        for (var i = 0; i < lineCount; i++)
        {
            FeedLine(lines[i].TrimEnd('\r'));
        }
        _lastLineAt = DateTime.UtcNow;
    }

    private void FeedLine(string line)
    {
        if (_pending is not null && !IsNewEntryStart(line))
        {
            _pending.Add(line);
            if (_pending.Count > MaxEntryLines) FlushPending();
            return;
        }
        if (_pending is not null) FlushPending();
        _pending = [line];
    }

    private void FlushPending()
    {
        var entry = string.Join("\n", _pending!);
        _pending = null;
        if (_patterns.Any(p => p.IsMatch(entry))) _onError(entry);
    }

    private static bool IsNewEntryStart(string line) => line.StartsWith('[');

    /// <summary>Mesmo comportamento de `errors="replace"` do Python — bytes inválidos viram U+FFFD em vez de derrubar o watcher.</summary>
    private static readonly Encoding Utf8ReplaceFallback =
        Encoding.GetEncoding("utf-8", EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
}
