﻿using Data;
using Data.Models.ProductTables;
using Lib;
using Lib.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers.SiteRoutes;

[Route("v1/site/categories")]
[ApiController]
public class CategoryController : ControllerBase
{
    private readonly Db db;

    public CategoryController(Db db)
    {
        this.db = db;
    }

    public record ShopCategoryOutput(string Id, string Name, string? ParentId);

    [HttpGet("{shopId}")]
    [Authorize]
    public async Task<IActionResult> ShopCategories([FromRoute] string shopId)
    {
        var uid = User.FindFirst(Jwt.Uid)?.Value;
        if (uid == null) Forbid();

        var categories = await db.Categories
            .Where(x => x.ShopId == shopId && x.Shop.OwnerId == uid)
            .Select(x => new ShopCategoryOutput(x.Id, x.Name, x.ParentId))
            .QueryMany();

        return Ok(categories);
    }

    public record AddCategoryInput(string Name, string ShopId, string? ParentId);

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> AddCategory([FromBody] AddCategoryInput input)
    {
        var uid = User.FindFirst(Jwt.Uid)?.Value;
        if (uid == null) Forbid();

        var ownShop = await db.Shops.Have(x => x.Id == input.ShopId && x.OwnerId == uid);
        if (!ownShop) return Forbid();

        var category = new Category(input.Name, input.ShopId, input.ParentId);
        await db.Categories.AddAsync(category);

        var saved = await db.Save();
        return saved ? Ok(new ShopCategoryOutput(category.Id, category.Name, category.ParentId)) : Problem();
    }

    [HttpDelete("{id}"), Authorize]
    public async Task<IActionResult> DeleteCategory([FromRoute] string id)
    {
        var uid = User.FindFirst(Jwt.Uid)?.Value;
        if (uid == null) Forbid();

        var category = await db.Categories.QueryOne(x => x.Id == id && x.Shop.OwnerId == uid);
        if (category == null) return NotFound();

        db.Remove(category);
        var saved = await db.Save();
        return saved ? Ok() : Problem();
    }

    /// <param name="ParentId">
    /// To cange value you should send NULL or Id.
    /// Empty string will be used as not edit value.
    /// </param>
    public record EditCategoryInput(string Id, string? Name, string? ParentId = "");

    [HttpPatch, Authorize]
    public async Task<IActionResult> EditCategory([FromBody] EditCategoryInput input)
    {
        var uid = User.FindFirst(Jwt.Uid)?.Value;
        if (uid == null) Forbid();

        var category = await db.Categories.QueryOne(x => x.Id == input.Id && x.Shop.OwnerId == uid);
        if (category == null) return NotFound();

        if (input.Name != null) category.Name = input.Name;

        var parentId = input.ParentId?.Trim();
        if (parentId == null)
        {
            category.ParentId = null;
        }
        else if (parentId != string.Empty)
        {
            var parentExist = await db.Categories.Have(x => x.Id == parentId && x.Shop.OwnerId == uid);
            if (parentExist) category.ParentId = parentId;
        }

        var saved = await db.Save();
        return saved ? Ok(new ShopCategoryOutput(category.Id, category.Name, category.ParentId)) : Problem();
    }
}