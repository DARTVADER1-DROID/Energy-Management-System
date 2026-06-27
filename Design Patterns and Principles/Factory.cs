using System;

namespace FactoryMethodPatternExample
{
    // Product Interface
    interface IDocument
    {
        void Open();
    }

    // Concrete Products
    class WordDocument : IDocument
    {
        public void Open()
        {
            Console.WriteLine("Opening Word Document...");
        }
    }

    class PdfDocument : IDocument
    {
        public void Open()
        {
            Console.WriteLine("Opening PDF Document...");
        }
    }

    class ExcelDocument : IDocument
    {
        public void Open()
        {
            Console.WriteLine("Opening Excel Document...");
        }
    }

    // Factory Base Class
    abstract class DocumentFactory
    {
        public abstract IDocument CreateDocument();
    }

    // Concrete Factories
    class WordDocumentFactory : DocumentFactory
    {
        public override IDocument CreateDocument()
        {
            return new WordDocument();
        }
    }

    class PdfDocumentFactory : DocumentFactory
    {
        public override IDocument CreateDocument()
        {
            return new PdfDocument();
        }
    }

    class ExcelDocumentFactory : DocumentFactory
    {
        public override IDocument CreateDocument()
        {
            return new ExcelDocument();
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            DocumentFactory factory;

            factory = new WordDocumentFactory();
            IDocument word = factory.CreateDocument();
            word.Open();

            factory = new PdfDocumentFactory();
            IDocument pdf = factory.CreateDocument();
            pdf.Open();

            factory = new ExcelDocumentFactory();
            IDocument excel = factory.CreateDocument();
            excel.Open();
        }
    }
}