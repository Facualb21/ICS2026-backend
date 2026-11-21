using System;
using System.Collections.Generic;
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
    public class OrderServiceTests
    {
        private readonly Mock<IRepository> _repositoryMock;
        private readonly Mock<ILogger<OrdersManagementService>> _loggerMock;
        private readonly OrdersManagementService _orderService;

        public OrderServiceTests()
        {
            _repositoryMock = new Mock<IRepository>();
            _loggerMock = new Mock<ILogger<OrdersManagementService>>();
            _orderService = new OrdersManagementService(_repositoryMock.Object, _loggerMock.Object);
        }

        #region Pruebas de Creacion de Ordenes (CA2)

        [Fact]
        public async Task CreateOrder_WithValidData_ReducesProductStockAndCreatesOrder()
        {
            var customerId = Guid.NewGuid();
            var productId = Guid.NewGuid();

            var customer = new Customer { Id = customerId, Name = "Juan Perez" };
            var product = new Product("SKU-100", "Mouse Gamer", 50.0m, 10, "Mouse optico", "INT-100")
            {
                Id = productId,
                IsActive = true
            };

            var orderRequest = new OrderRequest(
                customerId,
                "Calle Falsa 123",
                "Calle Falsa 123",
                "Entrega por la tarde",
                new List<OrderItemRequest>
                {
                    new OrderItemRequest(productId, 3)
                }
            );

            _repositoryMock.Setup(r => r.GetById<Customer>(customerId))
                .ReturnsAsync(customer);

            _repositoryMock.Setup(r => r.GetById<Product>(productId))
                .ReturnsAsync(product);

            _repositoryMock.Setup(r => r.Update(It.IsAny<Product>()))
                .ReturnsAsync((Product p) => p);

            _repositoryMock.Setup(r => r.Add(It.IsAny<Order>()))
                .ReturnsAsync((Order o) => o);

            var result = await _orderService.CreateOrder(orderRequest);

            result.Should().NotBeNull();
            result.ClientName.Should().Be("Juan Perez");
            //result.TotalAmount.Should().Be(150.0m);

            //Evidencia de bloqueo — CA2
            result.TotalAmount.Should().Be(999.0m);

            product.StockQuantity.Should().Be(7);

            _repositoryMock.Verify(r => r.Update(It.Is<Product>(p => p.StockQuantity == 7)), Times.Once);
            _repositoryMock.Verify(r => r.Add(It.IsAny<Order>()), Times.Once);
        }

        [Fact]
        public async Task CreateOrder_WithInsufficientStock_ThrowsInsufficientStockException()
        {
            var customerId = Guid.NewGuid();
            var productId = Guid.NewGuid();

            var customer = new Customer { Id = customerId, Name = "Maria Gomez" };
            var product = new Product("SKU-200", "Teclado Mecanico", 100.0m, 2, "Teclado RGB", "INT-200")
            {
                Id = productId,
                IsActive = true
            };

            var orderRequest = new OrderRequest(
                customerId,
                "Av. Siempre Viva 742",
                "Av. Siempre Viva 742",
                "",
                new List<OrderItemRequest>
                {
                    new OrderItemRequest(productId, 5)
                }
            );

            _repositoryMock.Setup(r => r.GetById<Customer>(customerId))
                .ReturnsAsync(customer);

            _repositoryMock.Setup(r => r.GetById<Product>(productId))
                .ReturnsAsync(product);

            Func<Task> action = async () => await _orderService.CreateOrder(orderRequest);

            await action.Should().ThrowAsync<InsufficientStockException>();

            product.StockQuantity.Should().Be(2);
            _repositoryMock.Verify(r => r.Update(It.IsAny<Product>()), Times.Never);
            _repositoryMock.Verify(r => r.Add(It.IsAny<Order>()), Times.Never);
        }

        [Fact]
        public async Task CreateOrder_WithNonExistentCustomer_ThrowsCustomerNotFoundException()
        {
            var customerId = Guid.NewGuid();
            var productId = Guid.NewGuid();

            var orderRequest = new OrderRequest(
                customerId,
                "Direccion 123",
                "Direccion 123",
                "",
                new List<OrderItemRequest>
                {
                    new OrderItemRequest(productId, 1)
                }
            );

            _repositoryMock.Setup(r => r.GetById<Customer>(customerId))
                .ReturnsAsync((Customer?)null);

            Func<Task> action = async () => await _orderService.CreateOrder(orderRequest);

            await action.Should().ThrowAsync<CustomerNotFoundException>();

            _repositoryMock.Verify(r => r.GetById<Product>(It.IsAny<Guid>()), Times.Never);
            _repositoryMock.Verify(r => r.Add(It.IsAny<Order>()), Times.Never);
        }

        #endregion
    }
}