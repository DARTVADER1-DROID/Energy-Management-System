using Microsoft.EntityFrameworkCore;
using RetailInventory.Data;
using RetailInventory.Models;
using var context=new AppDbContext();
await context.Database.EnsureCreatedAsync();
var electronics=new Category{Name="Electronics"};
var groceries=new Category{Name="Groceries"};
await context.Categories.AddRangeAsync(electronics,groceries);
await context.Products.AddRangeAsync(
 new Product{Name="Laptop",Price=75000,Category=electronics},
 new Product{Name="Rice Bag",Price=1200,Category=groceries});
await context.SaveChangesAsync();
var products=await context.Products.ToListAsync();
foreach(var p in products) Console.WriteLine($"{p.Name} - ₹{p.Price}");
var product=await context.Products.FindAsync(1);
Console.WriteLine($"Found: {product?.Name}");
var expensive=await context.Products.FirstOrDefaultAsync(p=>p.Price>50000);
Console.WriteLine($"Expensive: {expensive?.Name}");
