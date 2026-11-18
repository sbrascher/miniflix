using MiniFlix.Services;

namespace MiniFlix.Tests;

public class FilaServiceTests
{
    private static FilaItem Item(string titulo, Func<FilaItem, Relator, CancellationToken, Task> executor) =>
        new() { Tipo = "teste", Titulo = titulo, Executor = executor };

    private static async Task Esperar(FilaService fila, int ms = 5000)
    {
        var fim = DateTime.Now.AddMilliseconds(ms);
        while (fila.Ocupada && DateTime.Now < fim) await Task.Delay(20);
        Assert.False(fila.Ocupada, "a fila não terminou a tempo");
    }

    [Fact]
    public async Task Executa_um_item_por_vez_na_ordem()
    {
        var fila = new FilaService();
        var ordem = new List<string>();
        int simultaneos = 0, maximo = 0;
        Func<FilaItem, Relator, CancellationToken, Task> exec = async (item, _, _) =>
        {
            var agora = Interlocked.Increment(ref simultaneos);
            maximo = Math.Max(maximo, agora);
            await Task.Delay(50);
            lock (ordem) ordem.Add(item.Titulo);
            Interlocked.Decrement(ref simultaneos);
        };
        fila.Adicionar(Item("a", exec));
        fila.Adicionar(Item("b", exec));
        fila.Adicionar(Item("c", exec));
        await Esperar(fila);

        Assert.Equal(["a", "b", "c"], ordem);
        Assert.Equal(1, maximo);
        Assert.All(fila.Itens, i => Assert.Equal(EstadoFila.Concluido, i.Estado));
    }

    [Fact]
    public async Task Erro_em_um_item_nao_para_a_fila()
    {
        var fila = new FilaService();
        fila.Adicionar(Item("ruim", (_, _, _) => throw new InvalidOperationException("deu ruim")));
        fila.Adicionar(Item("bom", (_, _, _) => Task.CompletedTask));
        await Esperar(fila);

        var itens = fila.Itens;
        Assert.Equal(EstadoFila.Erro, itens[0].Estado);
        Assert.Equal("deu ruim", itens[0].Mensagem);
        Assert.Equal(EstadoFila.Concluido, itens[1].Estado);
    }

    [Fact]
    public async Task Cancelar_item_em_andamento_e_item_aguardando()
    {
        var fila = new FilaService();
        var iniciou = new TaskCompletionSource();
        var a = fila.Adicionar(Item("longo", async (_, _, ct) => { iniciou.SetResult(); await Task.Delay(10_000, ct); }));
        var b = fila.Adicionar(Item("esperando", (_, _, _) => Task.CompletedTask));

        await iniciou.Task;
        fila.Cancelar(b.Id);
        fila.Cancelar(a.Id);
        await Esperar(fila);

        Assert.All(fila.Itens, i => Assert.Equal(EstadoFila.Cancelado, i.Estado));
    }

    [Fact]
    public async Task Relator_mapeia_progresso_da_etapa_para_o_total()
    {
        var fila = new FilaService();
        var valores = new List<double>();
        fila.Adicionar(Item("x", (item, relator, _) =>
        {
            relator.Etapa("Convertendo", 0.2, 0.6);
            relator.Progresso(0.5);
            valores.Add(item.Progresso);
            return Task.CompletedTask;
        }));
        await Esperar(fila);
        Assert.Equal(0.4, valores[0], 3);
        Assert.Equal(1, fila.Itens[0].Progresso);
    }

    [Fact]
    public async Task Limpar_finalizados_remove_concluidos()
    {
        var fila = new FilaService();
        fila.Adicionar(Item("x", (_, _, _) => Task.CompletedTask));
        await Esperar(fila);
        fila.LimparFinalizados();
        Assert.Empty(fila.Itens);
    }
}
