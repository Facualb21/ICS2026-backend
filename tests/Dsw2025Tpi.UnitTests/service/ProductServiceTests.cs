using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Xunit;
using Moq;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Dsw2025Tpi.Application.Services;
using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Application.Exceptions;
using Dsw2025Tpi.Domain.Interfaces;
using Dsw2025Tpi.Domain.Entities;

namespace Dsw2025Tpi.UnitTests.Services
{
    public class ProductServiceTests
    {
        private readonly Mock<IRepository> _repositoryMock;
        private readonly Mock<ILogger<ProductsManagementService>> _loggerMock;
        private readonly ProductsManagementService _productService;

        public ProductServiceTests()
        {
            _repositoryMock = new Mock<IRepository>();
            _loggerMock = new Mock<ILogger<ProductsManagementService>>();
            _productService = new ProductsManagementService(_repositoryMock.Object, _loggerMock.Object);
        }

        #region Pruebas de Creacion de Productos (CA1)

        [Fact]
        public async Task AddProduct_WithValidData_ReturnsProductAndCallsRepositoryAdd()
        {
            var request = new ProductModel.Request(
                Sku: "SKU-123",
                InternalCode: "INT-001",
                Name: "Teclado Mecanico",
                Description: "Teclado RGB",
                CurrentUnitPrice: 150.50m,
                StockQuantity: 10
            );

            _repositoryMock.Setup(r => r.First<Product>(It.IsAny<Expression<Func<Product, bool>>>()))
                .ReturnsAsync((Product?)null);

            _repositoryMock.Setup(r => r.Add(It.IsAny<Product>()))
                .ReturnsAsync((Product p) => p);

            var result = await _productService.AddProduct(request);

            result.Should().NotBeNull();
            result.Sku.Should().Be("SKU-123");
            result.Name.Should().Be("Teclado Mecanico");

            _repositoryMock.Verify(r => r.Add(It.IsAny<Product>()), Times.Once);
        }

        [Fact]
        public async Task AddProduct_WithDuplicateSku_ThrowsDuplicatedEntityException()
        {
            var request = new ProductModel.Request(
                Sku: "SKU-DUPLICADO",
                InternalCode: "INT-OLD",
                Name: "Mouse",
                Description: "Mouse viejo",
                CurrentUnitPrice: 10m,
                StockQuantity: 5
            );

            var existingProduct = new Product("SKU-DUPLICADO", "Mouse Viejo", 10m, 5, "", "");

            _repositoryMock.Setup(r => r.First<Product>(It.IsAny<Expression<Func<Product, bool>>>()))
                .ReturnsAsync(existingProduct);

            Func<Task> action = async () => await _productService.AddProduct(request);

            await action.Should().ThrowAsync<DuplicatedEntityException>()
                .WithMessage("*SKU*");

            _repositoryMock.Verify(r => r.Add(It.IsAny<Product>()), Times.Never);
        }

        [Fact]
        public async Task AddProduct_WithInvalidStockOrPrice_ThrowsInvalidDataException()
        {
            var request = new ProductModel.Request(
                Sku: "SKU-999",
                InternalCode: "INT-999",
                Name: "Monitor",
                Description: "Monitor LED",
                CurrentUnitPrice: -50m,
                StockQuantity: 0
            );

            Func<Task> action = async () => await _productService.AddProduct(request);

            await action.Should().ThrowAsync<InvalidDataException>();

            _repositoryMock.Verify(r => r.First<Product>(It.IsAny<Expression<Func<Product, bool>>>()), Times.Never);
            _repositoryMock.Verify(r => r.Add(It.IsAny<Product>()), Times.Never);
        }

        #endregion

        #region Pruebas de Listado y Paginacion (CA3)

        [Fact]
        public async Task GetProducts_WhenIncludeInactiveIsFalse_ReturnsOnlyActiveProducts()
        {
            var mixedList = new List<Product>
            {
                new Product("SKU-1", "Activo", 10m, 10, "", "") { IsActive = true },
                new Product("SKU-2", "Inactivo", 10m, 10, "", "") { IsActive = false }
            };

            // Se agregan 2 parámetros al callback (predicate, _) porque IRepository.GetFiltered tiene 2 parámetros en su interfaz
            _repositoryMock.Setup(r => r.GetFiltered<Product>(It.IsAny<Expression<Func<Product, bool>>>()))
                .ReturnsAsync((Expression<Func<Product, bool>> predicate, object? _) =>
                {
                    return mixedList.AsQueryable().Where(predicate).ToList();
                });

            var filter = new ProductModel.FilterProduct(Status: null, Search: null, PageNumber: null, PageSize: null);

            var result = await _productService.GetProducts(filter, includeInactive: false);

            result.ProductItems.Should().HaveCount(1);
            result.ProductItems.First().Name.Should().Be("Activo");
        }

        [Fact]
        public async Task GetProducts_WithPaginationFilter_AppliesSkipAndTakeCorrectly()
        {
            var totalProducts = new List<Product>();
            for (int i = 1; i <= 15; i++)
            {
                totalProducts.Add(new Product($"SKU-{i}", $"Prod {i}", 10m, 10, "", "") { IsActive = true });
            }

            // Se agregan 2 parámetros al callback (predicate, _)
            _repositoryMock.Setup(r => r.GetFiltered<Product>(It.IsAny<Expression<Func<Product, bool>>>()))
                .ReturnsAsync((Expression<Func<Product, bool>> predicate, object? _) =>
                {
                    return totalProducts.AsQueryable().Where(predicate).ToList();
                });

            var filter = new ProductModel.FilterProduct(Status: null, Search: null, PageNumber: 2, PageSize: 5);

            var result = await _productService.GetProducts(filter);

            result.ProductItems.Should().HaveCount(5);
        }

        [Fact]
        public async Task GetProducts_WithSearchTerm_FiltersByNameSkuOrInternalCode()
        {
            var productList = new List<Product>
            {
                new Product("SKU-1", "Mouse Gamer", 10m, 10, "", "") { IsActive = true },
                new Product("SKU-2", "Teclado Mecanico", 10m, 10, "", "") { IsActive = true },
                new Product("SKU-3", "Monitor 24", 10m, 10, "", "") { IsActive = true }
            };

            // Se agregan 2 parámetros al callback (predicate, _)
            _repositoryMock.Setup(r => r.GetFiltered<Product>(It.IsAny<Expression<Func<Product, bool>>>()))
                .ReturnsAsync((Expression<Func<Product, bool>> predicate, object? _) =>
                {
                    return productList.AsQueryable().Where(predicate).ToList();
                });

            var filter = new ProductModel.FilterProduct(Status: null, Search: "Mouse", PageNumber: null, PageSize: null);

            var result = await _productService.GetProducts(filter);

            result.ProductItems.Should().HaveCount(1);
            result.ProductItems.First().Name.Should().Be("Mouse Gamer");
        }

        #endregion
    }
}
