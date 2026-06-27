using System;

namespace ECommerceSearch
{
    // Product Class
    public class Product
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; }
        public string Category { get; set; }

        public Product(int id, string name, string category)
        {
            ProductId = id;
            ProductName = name;
            Category = category;
        }

        public void Display()
        {
            Console.WriteLine($"ID: {ProductId}, Name: {ProductName}, Category: {Category}");
        }
    }

    class Program
    {
        // Linear Search
        static Product LinearSearch(Product[] products, int targetId)
        {
            foreach (Product product in products)
            {
                if (product.ProductId == targetId)
                    return product;
            }

            return null;
        }

        // Binary Search
        static Product BinarySearch(Product[] products, int targetId)
        {
            int left = 0;
            int right = products.Length - 1;

            while (left <= right)
            {
                int mid = left + (right - left) / 2;

                if (products[mid].ProductId == targetId)
                    return products[mid];

                if (products[mid].ProductId < targetId)
                    left = mid + 1;
                else
                    right = mid - 1;
            }

            return null;
        }

        static void Main(string[] args)
        {
            Product[] products =
            {
                new Product(101, "Laptop", "Electronics"),
                new Product(102, "Phone", "Electronics"),
                new Product(103, "Headphones", "Accessories"),
                new Product(104, "Keyboard", "Accessories"),
                new Product(105, "Mouse", "Accessories")
            };

            int searchId = 104;

            Console.WriteLine("----- Linear Search -----");

            Product linearResult = LinearSearch(products, searchId);

            if (linearResult != null)
                linearResult.Display();
            else
                Console.WriteLine("Product Not Found.");

            Console.WriteLine();

            Console.WriteLine("----- Binary Search -----");

            Product binaryResult = BinarySearch(products, searchId);

            if (binaryResult != null)
                binaryResult.Display();
            else
                Console.WriteLine("Product Not Found.");
        }
    }
}