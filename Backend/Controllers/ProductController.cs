﻿using Backend.Services;
using Data;
using Data.Models.ProductTables;
using Lib;
using Lib.EntityFrameworkCore;
using Lib.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers;

[Route("v1/products")]
[ApiController]
[Authorize]
public class ProductController : ControllerBase
{
    private readonly Db db;
    private readonly ImageService imageService;
    private readonly IStorage storage;

    public ProductController(Db db, ImageService imageService, IStorage storage)
    {
        this.db = db;
        this.imageService = imageService;
        this.storage = storage;
    }

    public record CreateProductInput(string Name, string SeoSlug, string ShopId);

    [HttpPost]
    public async Task<IActionResult> CreateProduct([FromBody] CreateProductInput newProduct)
    {
        var uid = User.FindFirst(Jwt.Uid)?.Value;
        var shop = await db.Shops.QueryOne(s => s.Id == newProduct.ShopId && s.OwnerId == uid);
        if (shop == null) return Forbid();

        var product = new Product(newProduct.Name, newProduct.SeoSlug, newProduct.ShopId);
        await db.Products.AddAsync(product);

        var saved = await db.Save();
        return saved ? Ok() : Problem();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Product>> GetProduct(string id)
    {
        var uid = User.FindFirst(Jwt.Uid)!.Value;
        var product = await db.Products.QueryOne(p => p.Id == id && p.Shop.OwnerId == uid);

        if (product == null) return NotFound();

        return Ok(product);
    }

    [HttpPatch]
    public async Task<IActionResult> UpdateProduct([FromBody] UpdateProductInput patchDoc)
    {
        var uid = User.FindFirst(Jwt.Uid)!.Value;
        var product = await db.Products.QueryOne(p => p.Id == patchDoc.Id && p.Shop.OwnerId == uid);

        if (product == null) return Problem();

        if (patchDoc.Name != null) product.Name = patchDoc.Name;
        if (patchDoc.Amount != 0) product.Amount = patchDoc.Amount;
        if (patchDoc.Price != 0) product.Price = patchDoc.Price;
        if (patchDoc.PreviewImage != null) product.PreviewImage = patchDoc.PreviewImage;
        if (patchDoc.VideoUrl != null) product.VideoUrl = patchDoc.VideoUrl;
        if (patchDoc.SeoTitle != null) product.SeoTitle = patchDoc.SeoTitle;
        if (patchDoc.SeoSlug != null) product.SeoSlug = patchDoc.SeoSlug;
        if (patchDoc.SeoDescription != null) product.SeoDescription = patchDoc.SeoDescription;

        var saved = await db.Save();

        return saved ? Ok(product) : Problem();
    }

    [HttpPut("{id}/publish")]
    public async Task<IActionResult> PublishProduct(string id)
    {
        var uid = User.FindFirst(Jwt.Uid)?.Value;
        var product = await db.Products.QueryOne(x => x.Id == id && x.Shop.OwnerId == uid);
        if (product == null) return NotFound();

        product.IsDraft = false;
        var saved = await db.Save();

        return saved ? Ok() : Problem();
    }

    [HttpPost("{id}/image")]
    public async Task<IActionResult> UploadProductImage([FromRoute] string id, [FromForm] IFormFile imageFile)
    {
        var uid = User.FindFirst(Jwt.Uid)!.Value;
        var product = await db.Products.QueryOne(x => x.Id == id && x.Shop.OwnerId == uid);
        if (product == null) return NotFound();

        if (!IsImageFile(imageFile)) return BadRequest();

        var fileId = $"{Guid.NewGuid()}{Path.GetExtension(imageFile.FileName)}";
        var uploadedFile = await storage.Upload(fileId, imageFile.OpenReadStream());
        if (uploadedFile == null) return Problem();

        var productImage = new ProductImage(uploadedFile.Provider, uploadedFile.Bucket, uploadedFile.Key, id);
        await db.ProductImages.AddAsync(productImage);
        var saved = await db.Save();

        if (saved) return Ok();

        await storage.Delete(productImage);
        return Problem();
    }

    [NonAction]
    public static bool IsImageFile(IFormFile file)
    {
        if (file == null || string.IsNullOrEmpty(file.FileName) || file.Length == 0) return false;

        var fileExtension = Path.GetExtension(file.FileName).ToLower();
        string[] photoExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".svg", ".webp", ".tiff", ".ico", }; ;
        return photoExtensions.Contains(fileExtension);
    }

    [HttpDelete("{id}/image")]
    public async Task<IActionResult> DeleteProductImage(string id)
    {
        var uid = User.FindFirst(Jwt.Uid)?.Value;
        var image = await db.ProductImages.QueryOne(pi => pi.Id == id && pi.Product.Shop.OwnerId == uid);
        if (image == null) return NotFound();

        bool isDeleted = await storage.Delete(image);
        if (!isDeleted) return Problem();

        db.ProductImages.Remove(image);
        var saved = await db.Save();
        return saved ? Ok() : Problem();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteProduct(string id)
    {
        var uid = User.FindFirst(Jwt.Uid)?.Value;
        var product = await db.Products.QueryOne(p => p.Id == id && p.Shop.OwnerId == uid);

        if (product == null) return Problem();

        bool canDeleteProduct = !await db.Orders.AnyAsync(o => o.ProductId == id);

        if (canDeleteProduct)
        {
            var images = await db.ProductImages.QueryMany(x => x.ProductId == product.Id);
            await imageService.SafeDelete(images);
            db.Products.Remove(product);
        }
        else
        {
            product.IsArchive = true;
        }

        var saved = await db.Save();
        return saved ? Ok() : Problem();
    }
}

public class UpdateProductInput
{
    public string Id { get; set; } = string.Empty;
    public string? Name { get; set; }
    public double Price { get; set; } = 0;
    public int Amount { get; set; } = 0;
    public string? PreviewImage { get; set; }
    public string? VideoUrl { get; set; }
    public string? SeoTitle { get; set; }
    public string? SeoDescription { get; set; }
    public string? SeoSlug { get; set; }
}