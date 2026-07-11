using Microsoft.EntityFrameworkCore;
using RetailInventory.Models;
namespace RetailInventory.Data;
public class AppDbContext:DbContext{
 public DbSet<Product> Products=>Set<Product>();
 public DbSet<Category> Categories=>Set<Category>();
 protected override void OnConfiguring(DbContextOptionsBuilder b)=>
  b.UseSqlServer("Server=.;Database=RetailInventoryDB;Trusted_Connection=True;TrustServerCertificate=True;");
}