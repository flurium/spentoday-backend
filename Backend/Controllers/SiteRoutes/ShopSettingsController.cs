﻿using Backend.Services;
using Data;
using Data.Models.ShopTables;
using Lib.EntityFrameworkCore;
using Lib;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Data.Models.ProductTables;
using Lib.Storage;
using Data.Models.UserTables;

namespace Backend.Controllers.SiteRoutes
{
    [Route("v1/site/shopsettings")]
    [ApiController]
    public class ShopSettingsController : ControllerBase
    {
        private readonly Db db;
        private readonly ImageService imageService;

        private readonly IStorage storage;

        public ShopSettingsController(Db context, ImageService imageService, IStorage storage)
        {
            db = context;
            this.imageService = imageService;
            this.storage = storage;
        }
        public record LinkIn(string Name, string Link);
        public record LinkOut(string Name, string Link, string Id);
        public record BannerOut(string Url, string Id);
        public record ShopUpdate(string Name);
        public record ShopOut(string Name, string Logo, List<BannerOut> Banners, List<LinkOut> Links);
        [HttpPost("{shopId}/link")]
        [Authorize]
        public async Task<IActionResult> AddLink([FromBody] LinkIn link, [FromRoute] string shopId)
        {
            var uid = User.FindFirst(Jwt.Uid);
            if (uid == null) return Unauthorized();

            var shop = await db.Shops
            .QueryOne(x => x.Id == shopId && x.OwnerId == uid.Value);

            if (shop == null) return Problem();

            var newLink = new SocialMediaLink(link.Name, link.Link, shopId);

            await db.SocialMediaLinks.AddAsync(newLink);

            var saved = await db.Save();
            return saved ? Ok(new LinkOut(newLink.Name, newLink.Link, newLink.Id)) : Problem();
        }
        [HttpDelete("link/{linkId}")]
        [Authorize]
        public async Task<IActionResult> DeleteLink([FromRoute] string linkId)
        {
            var uid = User.FindFirst(Jwt.Uid);
            if (uid == null) return Unauthorized();

            var link = await db.SocialMediaLinks
           .Where(x => x.Id == linkId)
           .QueryOne();

            if (link == null) return NotFound();

            db.SocialMediaLinks.Remove(link);

            var saved = await db.Save();
            return saved ? Ok() : Problem();
        }
        [HttpPost("{shopId}/banner")]
        [Authorize]
        public async Task<IActionResult> AddBanner(IFormFile file, [FromRoute] string shopId)
        {
            var uid = User.FindFirst(Jwt.Uid);
            if (uid == null) return Unauthorized();

            var banner = file;
            if (!ImageExtension.IsImage(banner)) return BadRequest();

            var shop = await db.Shops
            .QueryOne(x => x.Id == shopId && x.OwnerId == uid.Value);

            if (shop == null) return Problem();

            var fileId = Guid.NewGuid().ToString() + Path.GetExtension(banner.FileName);
            ShopBanner shopBanner;

            using (var stream = banner.OpenReadStream())
            {
                var uploadedFile = await storage.Upload(fileId, stream);

                if (uploadedFile == null) return Problem();

                shopBanner = new ShopBanner(uploadedFile.Provider, uploadedFile.Bucket, uploadedFile.Key, shopId);
                await db.ShopBanners.AddAsync(shopBanner);
            }

            var saved = await db.Save();

            if (!saved)
            {
                await imageService.SafeDelete(shopBanner);
                return Problem();
            }
            return Ok(new BannerOut(storage.Url(shopBanner.GetStorageFile()), shopBanner.Id));
        }
        [HttpDelete("banner/{bannerId}")]
        [Authorize]
        public async Task<IActionResult> DeleteBanner([FromRoute] string bannerId)
        {
            var uid = User.FindFirst(Jwt.Uid);
            if (uid == null) return Unauthorized();

            var banner = await db.ShopBanners
           .Where(x => x.Id == bannerId)
           .QueryOne();

            if (banner == null) return Problem();

            await imageService.SafeDelete(banner);
            db.ShopBanners.Remove(banner);
            var saved = await db.Save();
            return saved ? Ok() : Problem();
        }
        [HttpPost("{shopId}/name")]
        [Authorize]
        public async Task<IActionResult> UpdateShopName([FromRoute] string shopId, [FromBody] ShopUpdate shopName)
        {
            var uid = User.FindFirst(Jwt.Uid);
            if (uid == null) return Unauthorized();

            var shop = await db.Shops
           .QueryOne(x => x.Id == shopId && x.OwnerId == uid.Value);

            if (shop == null) return NotFound();

            shop.Name = shopName.Name;

            var saved = await db.Save();
            return saved ? Ok() : Problem();
        }
        [HttpPost("{shopId}/logo")]
        [Authorize]
        public async Task<IActionResult> UploadLogo([FromRoute] string shopId, IFormFile file)
        {
            var uid = User.FindFirst(Jwt.Uid);
            if (uid == null) return Unauthorized();

            var shop = await db.Shops
           .QueryOne(x => x.Id == shopId && x.OwnerId == uid.Value);

            if (shop == null) return Problem();

            if (ImageExtension.IsImage(file))
            {
                var logo = shop.GetStorageFile();
                if (logo != null)
                {
                    await imageService.SafeDelete(logo);
                }

                var fileId = Guid.NewGuid().ToString() + Path.GetExtension(file.FileName);

                using (var stream = file.OpenReadStream())
                {
                    var uploadedFile = await storage.Upload(fileId, stream);

                    if (uploadedFile == null) return Problem();

                    shop.LogoBucket = uploadedFile.Bucket;
                    shop.LogoKey = uploadedFile.Key;
                    shop.LogoProvider = uploadedFile.Provider;
                }
            }
            else return BadRequest();

            var saved = await db.Save();
            return saved ? Ok(storage.Url(shop.GetStorageFile())) : Problem();
        }
        [HttpGet("shop/{shopId}")]
        [Authorize]
        public async Task<IActionResult> GetShop([FromRoute] string shopId)
        {
            var uid = User.FindFirst(Jwt.Uid);
            if (uid == null) return Unauthorized();

            var shop = await db.Shops
           .QueryOne(x => x.Id == shopId && x.OwnerId == uid.Value);

            if (shop == null) return NotFound();

            var banners = await db.ShopBanners
            .Where(x => x.ShopId == shopId)
            .Select(x => new BannerOut(storage.Url(x.GetStorageFile()), x.Id))
            .QueryMany();

            var links = await db.SocialMediaLinks
           .Where(x => x.ShopId == shopId)
           .Select(x => new LinkOut(x.Name, x.Link, x.Id))
           .QueryMany();

            var logoFile = shop.GetStorageFile();
            string logo = "https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcRDsRxTnsSBMmVvRxdygcb9ue6xfUYL58YX27JLNLohHQ&s";
            if (logoFile != null) logo = storage.Url(logoFile);

            string name = shop.Name;

            var shopOut = new ShopOut(name, logo, banners.ToList(), links.ToList());

            var saved = await db.Save();
            return saved ? Ok(shopOut) : Problem();
        }
    }
}
