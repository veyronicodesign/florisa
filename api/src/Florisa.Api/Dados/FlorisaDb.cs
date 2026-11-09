using Florisa.Api.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Florisa.Api.Dados;

public class FlorisaDb : DbContext
{
    public FlorisaDb(DbContextOptions<FlorisaDb> options) : base(options) { }

    public DbSet<Produto> Produtos => Set<Produto>();
    public DbSet<Extra> Extras => Set<Extra>();
    public DbSet<TaxaEntrega> TaxasEntrega => Set<TaxaEntrega>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Pedido> Pedidos => Set<Pedido>();
    public DbSet<PedidoItem> PedidoItens => Set<PedidoItem>();
    public DbSet<PedidoEvento> PedidoEventos => Set<PedidoEvento>();
    public DbSet<CodigoAcesso> CodigosAcesso => Set<CodigoAcesso>();
    public DbSet<MembroEquipe> Equipe => Set<MembroEquipe>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Produto>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(40);
            e.Property(x => x.Nome).HasMaxLength(160);
            e.Property(x => x.Categoria).HasMaxLength(40);
            e.Property(x => x.Preco).HasPrecision(10, 2);
            e.Property(x => x.PrecoDe).HasPrecision(10, 2);
        });

        b.Entity<Extra>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(40);
            e.Property(x => x.Preco).HasPrecision(10, 2);
        });

        b.Entity<TaxaEntrega>(e =>
        {
            e.HasKey(x => x.Bairro);
            e.Property(x => x.Bairro).HasMaxLength(80);
            e.Property(x => x.Valor).HasPrecision(10, 2);
        });

        b.Entity<Cliente>(e =>
        {
            e.HasIndex(x => x.Telefone).IsUnique();
            e.Property(x => x.Telefone).HasMaxLength(13);
        });

        b.Entity<Pedido>(e =>
        {
            e.HasIndex(x => x.Numero).IsUnique();
            e.HasIndex(x => x.ClienteTelefone);
            e.HasIndex(x => x.PagamentoGatewayId);
            e.HasIndex(x => new { x.Etapa, x.DataEntrega });
            e.Property(x => x.Numero).HasMaxLength(12);
            e.Property(x => x.Subtotal).HasPrecision(10, 2);
            e.Property(x => x.Frete).HasPrecision(10, 2);
            e.Property(x => x.Total).HasPrecision(10, 2);
            e.HasOne(x => x.Cliente).WithMany().HasForeignKey(x => x.ClienteId);
            e.HasMany(x => x.Itens).WithOne().HasForeignKey(x => x.PedidoId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Eventos).WithOne().HasForeignKey(x => x.PedidoId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PedidoItem>(e =>
        {
            e.Property(x => x.PrecoUnitario).HasPrecision(10, 2);
            e.Property(x => x.ValorExtras).HasPrecision(10, 2);
            e.Property(x => x.Total).HasPrecision(10, 2);
        });

        b.Entity<CodigoAcesso>(e => e.HasIndex(x => new { x.Telefone, x.CriadoEm }));

        b.Entity<MembroEquipe>(e => e.HasIndex(x => x.Usuario).IsUnique());
    }
}
