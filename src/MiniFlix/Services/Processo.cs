using System.Diagnostics;
using System.Text;

namespace MiniFlix.Services;

public sealed record ResultadoProcesso(int CodigoSaida, string UltimasLinhasErro);

/// <summary>Executa processos externos sem janela, lendo a saída linha a linha. Cancelar mata a árvore de processos.</summary>
public static class Processo
{
    public static async Task<ResultadoProcesso> ExecutarAsync(
        string exe,
        IEnumerable<string> argumentos,
        CancellationToken ct,
        Action<string>? aoLerSaida = null,
        Action<string>? aoLerErro = null,
        IDictionary<string, string>? ambiente = null)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in argumentos) psi.ArgumentList.Add(a); // sem problemas de aspas/espaços
        if (ambiente is not null)
            foreach (var (k, v) in ambiente) psi.Environment[k] = v;

        using var p = new Process { StartInfo = psi };
        var erros = new Queue<string>();
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) aoLerSaida?.Invoke(e.Data); };
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (erros)
            {
                erros.Enqueue(e.Data);
                if (erros.Count > 25) erros.Dequeue();
            }
            aoLerErro?.Invoke(e.Data);
        };

        Log.Info($"Executando: {Path.GetFileName(exe)} {string.Join(' ', psi.ArgumentList.Select(a => a.Contains(' ') ? $"\"{a}\"" : a))}");
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        await using (ct.Register(() => { try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { } }))
            await p.WaitForExitAsync(CancellationToken.None);

        ct.ThrowIfCancellationRequested();
        string ultimas;
        lock (erros) ultimas = string.Join('\n', erros);
        return new ResultadoProcesso(p.ExitCode, ultimas);
    }
}
