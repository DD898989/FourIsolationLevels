using Microsoft.EntityFrameworkCore;
using MyApiApp.Models;

namespace MyApiApp.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Account> Accounts { get; set; } = null!;
}
