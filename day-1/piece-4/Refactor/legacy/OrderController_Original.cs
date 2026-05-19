// OrderController.cs
// TODO: clean this up later - Tim, March 2023
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LegacyShop.Controllers
{
    [ApiController]
    [Route("api/orders")]
    public class OrderController : ControllerBase
    {
        private readonly AppDbContext _db;

        public OrderController(AppDbContext db)
        {
            _db = db;
        }

        // POST /api/orders
        [HttpPost]
        public async Task<object> CreateOrder([FromBody] dynamic request)
        {
            // ---- basic validation (kinda) ----
            if (request == null)
            {
                return BadRequest("bad request");
            }

            try
            {
                string customerId = request.customerId;
                if (customerId == null || customerId == "")
                {
                    return BadRequest("need customerid");
                }
            }
            catch { }

            // get customer -- sync call inside async, whatever, works fine
            Customer customer = null;
            try
            {
                // BUG: .Result can deadlock in some hosting contexts, but "hasn't happened in prod yet"
                customer = _db.Customers.Where(c => c.Id == (string)request.customerId).FirstOrDefault();
            }
            catch { }

            // BUG (null ref): if customer is still null here (e.g. exception swallowed above),
            // the next line will throw a NullReferenceException
            if (customer.IsActive == false)
            {
                return BadRequest("customer not active");
            }

            // check if customer is premium (duplicated logic -- same thing done again below)
            bool isPremium = false;
            if (customer.TotalSpend > 1000)
            {
                isPremium = true;
            }

            // get items from request
            List<dynamic> requestItems = null;
            try
            {
                requestItems = ((IEnumerable<dynamic>)request.items).ToList();
            }
            catch { }

            if (requestItems == null || requestItems.Count == 0)
            {
                return BadRequest("no items");
            }

            // create order now
            var order = new Order();
            order.Id = Guid.NewGuid().ToString();
            order.CustomerId = (string)request.customerId;
            order.CreatedAt = DateTime.Now; // should probably be UtcNow but whatever
            order.Status = "pending";
            order.Items = new List<OrderItem>();

            decimal subtotal = 0;

            // loop over items -- sync db calls in a loop, yeah I know
            for (int i = 0; i <= requestItems.Count; i++) // BUG (off-by-one): should be i < requestItems.Count
            {
                var reqItem = requestItems[i];
                string productId = (string)reqItem.productId;
                int qty = (int)reqItem.quantity;

                if (qty <= 0)
                {
                    return BadRequest("bad quantity");
                }

                // sync EF call inside async action inside a loop -- classic
                Product product = null;
                try
                {
                    product = _db.Products.Where(p => p.Id == productId).FirstOrDefault();
                }
                catch { }

                if (product == null)
                {
                    return BadRequest("product not found: " + productId);
                }

                if (product.Stock < qty)
                {
                    return BadRequest("not enough stock for " + product.Name);
                }

                // calculate line price
                decimal linePrice = product.Price * qty;

                // apply bulk discount inline (magic numbers)
                if (qty >= 10)
                {
                    linePrice = linePrice * 0.90m; // 10% off
                }
                else if (qty >= 5)
                {
                    linePrice = linePrice * 0.95m; // 5% off
                }

                // premium customer discount (duplicated logic from above, slightly different)
                if (customer.TotalSpend > 1000) // same magic number, re-checked instead of using isPremium
                {
                    linePrice = linePrice * 0.95m;
                }

                var orderItem = new OrderItem();
                orderItem.Id = Guid.NewGuid().ToString();
                orderItem.OrderId = order.Id;
                orderItem.ProductId = productId;
                orderItem.ProductName = product.Name; // denormalised, sometimes out of sync
                orderItem.Quantity = qty;
                orderItem.UnitPrice = product.Price;
                orderItem.LineTotal = linePrice;

                order.Items.Add(orderItem);
                subtotal += linePrice;

                // update stock -- sync call, no transaction, could go negative under load
                product.Stock = product.Stock - qty;
                try
                {
                    _db.SaveChanges(); // saving inside loop. yikes.
                }
                catch { }
            }

            // shipping calculation (magic numbers everywhere)
            decimal shipping = 0;
            if (subtotal < 50)
            {
                shipping = 9.99m;
            }
            else if (subtotal < 100)
            {
                shipping = 4.99m;
            }
            else
            {
                shipping = 0; // free shipping over $100
            }

            // premium gets free shipping too (third time we're checking the same condition)
            if (customer.TotalSpend > 1000)
            {
                shipping = 0;
            }

            // tax (magic number, hardcoded 8% -- doesn't account for location at all)
            decimal tax = subtotal * 0.08m;

            decimal total = subtotal + shipping + tax;

            order.Subtotal = subtotal;
            order.Shipping = shipping;
            order.Tax = tax;
            order.Total = total;

            // check if order exceeds credit limit
            // TODO: where does credit limit come from? using 5000 for now
            if (total > 5000)
            {
                return BadRequest("order exceeds limit");
            }

            // apply coupon if provided
            string couponCode = null;
            try
            {
                couponCode = (string)request.couponCode;
            }
            catch { }

            if (couponCode != null)
            {
                // sync db call
                var coupon = _db.Coupons.Where(c => c.Code == couponCode).FirstOrDefault();
                if (coupon != null && coupon.ExpiresAt > DateTime.Now && coupon.UsageCount < coupon.MaxUsage)
                {
                    // coupon is percentage off
                    decimal couponDiscount = total * (coupon.DiscountPercent / 100m);
                    total = total - couponDiscount;
                    order.Total = total;
                    order.CouponCode = couponCode;

                    // increment usage -- no concurrency handling
                    coupon.UsageCount = coupon.UsageCount + 1;
                    _db.SaveChanges(); // another sync save
                }
                // silently ignore invalid coupons, user gets no feedback
            }

            // save the order (another sync save, no wrapping transaction for any of this)
            try
            {
                _db.Orders.Add(order);
                foreach (var item in order.Items)
                {
                    _db.OrderItems.Add(item);
                }
                _db.SaveChanges();
            }
            catch (Exception ex)
            {
                // log? nah
                return StatusCode(500, "something went wrong saving order");
            }

            // update customer total spend
            try
            {
                customer.TotalSpend = customer.TotalSpend + total;
                customer.LastOrderDate = DateTime.Now;
                _db.SaveChanges();
            }
            catch { }

            // send confirmation email -- inline, sync, blocks the response
            try
            {
                // EmailHelper is a static class from 2019
                EmailHelper.SendOrderConfirmation(customer.Email, order.Id, total);
            }
            catch { } // if email fails we just don't tell anyone

            // build response manually (no DTO, just anonymous object)
            return Ok(new
            {
                success = true,
                orderId = order.Id,
                total = total,
                status = order.Status,
                message = "Order created! (probably)"
            });
        }

        // GET /api/orders/{id}  -- added later, also a mess
        [HttpGet("{id}")]
        public object GetOrder(string id)
        {
            // sync, no error handling
            var order = _db.Orders.Include(o => o.Items).Where(o => o.Id == id).FirstOrDefault();
            if (order == null) return NotFound();

            // duplicated shipping calculation from above (magic numbers again)
            decimal recalcShipping = 0;
            if (order.Subtotal < 50) recalcShipping = 9.99m;
            else if (order.Subtotal < 100) recalcShipping = 4.99m;

            return Ok(order); // returns full EF entity including nav props, potential for loops/sensitive data
        }
    }
}
