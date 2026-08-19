using System;
using System.Linq;
using CoreCms.Net.IRepository.UnitOfWork;
using CoreCms.Net.Model.Entities;
using CoreCms.Net.Model.ViewModels.UI;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;

namespace CoreCms.Net.Web.WebApi.Controllers
{
    /// <summary>
    /// 鲜鱼接龙专用商品目录接口。
    /// 一次返回商品、默认 SKU 与鲜鱼专属库存/售卖规则。
    /// </summary>
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class SeafoodCatalogController : ControllerBase
    {
        private readonly SqlSugarScope _db;

        public SeafoodCatalogController(IUnitOfWork unitOfWork)
        {
            _db = unitOfWork.GetDbClient();
            _db.CodeFirst.InitTables<SeafoodProductConfigRecord>();
        }

        /// <summary>
        /// 获取当前已启用的鲜鱼商品。不要求商城登录。
        /// </summary>
        [HttpPost]
        public WebApiCallBack List()
        {
            var jm = new WebApiCallBack();

            try
            {
                var goods = _db.Queryable<CoreCmsGoods>()
                    .Where(g => g.goodsCategoryId == 2058 && g.isDel == false && g.isMarketable == true)
                    .OrderBy(g => g.sort, OrderByType.Asc)
                    .OrderBy(g => g.id, OrderByType.Asc)
                    .ToList();

                if (goods.Count == 0)
                {
                    jm.status = true;
                    jm.msg = "获取成功";
                    jm.data = Array.Empty<object>();
                    return jm;
                }

                var goodsIds = goods.Select(g => g.id).ToList();
                var products = _db.Queryable<CoreCmsProducts>()
                    .Where(p => goodsIds.Contains(p.goodsId)
                                && p.isDel == false
                                && p.marketable == true
                                && p.isDefalut == true)
                    .ToList();

                var productMap = products
                    .GroupBy(p => p.goodsId)
                    .ToDictionary(g => g.Key, g => g.OrderBy(p => p.id).First());

                var productIds = products.Select(p => p.id).ToList();
                var configs = productIds.Count == 0
                    ? new System.Collections.Generic.List<SeafoodProductConfigRecord>()
                    : _db.Queryable<SeafoodProductConfigRecord>()
                        .Where(c => productIds.Contains(c.productId) && c.enabled == true)
                        .ToList();

                var configMap = configs.ToDictionary(c => c.productId, c => c);

                var rows = goods
                    .Where(g => productMap.ContainsKey(g.id))
                    .Select(g => new { goods = g, product = productMap[g.id] })
                    .Where(x => configMap.ContainsKey(x.product.id))
                    .Select(x =>
                    {
                        var g = x.goods;
                        var p = x.product;
                        var c = configMap[p.id];
                        var saleMode = SeafoodSaleModes.IsSupported(c.saleMode)
                            ? c.saleMode
                            : SeafoodSaleModes.WholeJin;
                        var saleStep = SeafoodSaleModes.GetStep(saleMode);
                        var unit = SeafoodSaleModes.GetUnit(saleMode);
                        var available = Math.Max(0m, c.stockQty - c.freezeQty);

                        return new
                        {
                            id = g.id,
                            productId = p.id,
                            name = g.name ?? string.Empty,
                            brief = g.brief ?? string.Empty,
                            image = string.IsNullOrWhiteSpace(g.image)
                                ? "/static/images/common/empty-banner.png"
                                : g.image,
                            unit,
                            saleMode,
                            saleStep,
                            price = p.price,
                            mktprice = p.mktprice,
                            stock = c.stockQty,
                            freezeStock = c.freezeQty,
                            availableStock = available,
                            sort = g.sort
                        };
                    })
                    .ToList();

                jm.status = true;
                jm.msg = "获取成功";
                jm.data = rows;
                return jm;
            }
            catch
            {
                jm.status = false;
                jm.msg = "鱼货目录读取失败，请稍后重试";
                return jm;
            }
        }
    }
}
