﻿using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Contracts;
using ExactlyOnce.NServiceBus;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using NServiceBus;
using Orders.DomainModel;
using Orders.Models;

namespace Orders.Controllers
{
    public class OrderController : Controller
    {
        readonly ILogger<OrderController> logger;
        readonly Container ordersContainer;
        readonly IHumanInterfaceConnector<string> connector;

        public OrderController(ILogger<OrderController> logger, Container ordersContainer, IHumanInterfaceConnector<string> connector)
        {
            this.logger = logger;
            this.ordersContainer = ordersContainer;
            this.connector = connector;
        }

        [Route("order/{customerId}/{orderId}")]
        [HttpGet]
        public async Task<IActionResult> Index(string customerId, string orderId)
        {
            var orderResponse = await ordersContainer.ReadItemAsync<Order>(orderId, new PartitionKey(customerId));
            return View(orderResponse.Resource);
        }

        [Route("order/add-item/{customerId}/{orderId}")]
        [HttpGet]
        public IActionResult AddItem(string customerId, string orderId)
        {
            return View();
        }

        [Route("order/add-item/{customerId}/{orderId}")]
        [HttpPost]
        public async Task<IActionResult> AddItem(string customerId, string orderId, Item item)
        {
            var orderResponse = await ordersContainer.ReadItemAsync<Order>(orderId, new PartitionKey(customerId));
            var order = orderResponse.Resource;

            order.Items.Add(item);

            await ordersContainer.ReplaceItemAsync(order, orderId, new PartitionKey(customerId));

            return RedirectToAction("Index", new { customerId, orderId });
        }

        [Route("order/submit/{customerId}/{orderId}")]
        [HttpPost]
        public async Task<IActionResult> Submit(string customerId, string orderId)
        {
            var requestId = Guid.NewGuid().ToString(); //TODO
            var result = await connector.ExecuteTransaction<IActionResult>(requestId, customerId, async session =>
            {
                var orderResponse = await session.TransactionContext.Batch().ReadItemAsync<Order>(orderId);
                var order = orderResponse.Resource;

                if (order.State != OrderState.Created)
                {
                    ModelState.AddModelError("State", "Cannot submit an order that is not in the Created state");
                    return View("Index", order);
                }

                order.State = OrderState.Submitted;

                session.TransactionContext.Batch().ReplaceItem(orderId, order);
                await session.Send(new BillCustomer
                {
                    OrderId = orderId,
                    CustomerId = customerId,
                    Items = order.Items.Select(x => new OrderItem
                    {
                        Product = x.Product,
                        Count = x.Count,
                        Value = x.Value
                    }).ToList()
                });

                return RedirectToAction("Index", "Order", new { customerId, orderId });
            });
            return result;
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
