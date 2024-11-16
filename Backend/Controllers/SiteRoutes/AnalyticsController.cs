using Amazon.S3.Model;
using Backend.Auth;
using Backend.Services;
using Data;
using Lib.EntityFrameworkCore;
using Lib.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static Backend.Controllers.SiteRoutes.DashboardController;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace Backend.Controllers.SiteRoutes;

[Route("v1/site/analytics")]
[ApiController]
public class AnalyticsController : ControllerBase
{
    private readonly Db db;

    public AnalyticsController(Db db)
    {
        this.db = db;
    }

    public record AnalyticsProduct(string Id, string Name, double Price, int Count);

    public record AnalyticsOutput(
        int TotalOrders, int OrdersLastMonth,
        List<AnalyticsProduct> PopularProducts,
        double TotalMoney, double LastMonthMoney
    );

    [HttpGet("{shopId}")]
    public async Task<IActionResult> Index([FromRoute] string shopId)
    {
        var uid = User.Uid();

        var products = await db.Products
            .Where(x => x.ShopId == shopId && x.Shop.OwnerId == uid)
            .OrderByDescending(x => x.OrderProducts.Count())
            .Take(10)
            .Select(x => new AnalyticsProduct(x.Id, x.Name, x.Price, x.OrderProducts.Count()))
            .QueryMany();

        var totalOrders = await db.Orders.CountAsync();
        var monthAgo = DateTime.Now.AddDays(-30).ToUniversalTime();
        var ordersLastMonth = await db.Orders
            .Where(x => x.Date.ToUniversalTime() >= monthAgo).CountAsync();

        var moneyQuery = db.OrderProducts
            .Where(x => x.Order.SellerShopId == shopId)
            .Where(x => x.Order.Status == "Виконано");

        var totalMoney = await moneyQuery.SumAsync(x => x.Price * x.Amount);
        var lastMonthMoney = await moneyQuery
            .Where(x => x.Order.Date.ToUniversalTime() >= monthAgo)
            .SumAsync(x => x.Price * x.Amount);

        return Ok(new AnalyticsOutput(
            totalOrders, ordersLastMonth, products, totalMoney, lastMonthMoney
        ));
    }
}