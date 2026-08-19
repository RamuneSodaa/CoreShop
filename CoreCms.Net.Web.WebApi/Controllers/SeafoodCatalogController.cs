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
    /// 一次返回商品与默认 SKU，避免顾客端逐个调用通用商品详情接口。
    /// </summary>
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class SeafoodCatalogController : ControllerBase
    {
        private readonly SqlSugarScope _db;

        public SeafoodCatalogController(IUnitOfWork unitOfWork)
        {
            _db = unitOfWork.GetDbClient();
        }

        /// <summary>
        /// 获取当前水产海鲜分类中已上架、未删除、存在默认 SKU 的商品。
        /// 不要求商城登录。
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

                var rows = goods
                    .Where(g => productMap.ContainsKey(g.id))
                    .Select(g =>
                    {
                        var p = productMap[g.id];
                        return new
                        {
                            id = g.id,
                            productId = p.id,
                            name = g.name ?? string.Empty,
                            brief = g.brief ?? string.Empty,
                            image = string.IsNullOrWhiteSpace(g.image)
                                ? "/static/images/common/empty-banner.png"
                                : g.image,
                            unit = string.IsNullOrWhiteSpace(g.unit) ? "斤" : g.unit,
                            price = p.price,
                            mktprice = p.mktprice,
                            stock = p.stock,
                            freezeStock = p.freezeStock,
                            availableStock = Math.Max(0, p.stock - p.freezeStock),
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
