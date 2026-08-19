using System;
using System.Collections.Generic;
using System.ComponentModel;
using CoreCms.Net.IRepository.UnitOfWork;
using CoreCms.Net.Model.Entities;
using CoreCms.Net.Model.ViewModels.UI;
using CoreCms.Net.Web.Admin.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;

namespace CoreCms.Net.Web.Admin.Controllers
{
    /// <summary>
    /// 鲜鱼商品售卖设置。
    /// 独立维护小数库存与售卖步进，不修改 CoreShop 原整数库存字段。
    /// </summary>
    [Description("鲜鱼商品售卖设置")]
    [Route("api/[controller]/[action]")]
    [ApiController]
    [RequiredErrorForAdmin]
    [Authorize]
    public class SeafoodProductConfigController : ControllerBase
    {
        private const int SeafoodCategoryId = 2058;
        private const string WholeJin = "whole_jin";
        private const string HalfJin = "half_jin";
        private const string Piece = "piece";

        private readonly IUnitOfWork _unitOfWork;
        private readonly SqlSugarScope _db;

        public SeafoodProductConfigController(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
            _db = unitOfWork.GetDbClient();
            _db.CodeFirst.InitTables<SeafoodProductConfigAdminEditRecord>();
        }

        /// <summary>
        /// 按商品读取鲜鱼设置；没有配置时返回安全默认值但不自动启用。
        /// </summary>
        [HttpPost]
        public AdminUiCallBack GetByGoodsId(int goodsId)
        {
            if (goodsId <= 0)
            {
                return Fail("商品ID无效");
            }

            var goods = _db.Queryable<CoreCmsGoods>()
                .Where(g => g.id == goodsId && g.isDel == false)
                .First();
            if (goods == null)
            {
                return Fail("商品不存在");
            }

            var product = _db.Queryable<CoreCmsProducts>()
                .Where(p => p.goodsId == goodsId && p.isDel == false && p.isDefalut == true)
                .OrderBy(p => p.id, OrderByType.Asc)
                .First();
            if (product == null)
            {
                return Fail("商品缺少默认货品，无法设置鲜鱼库存");
            }

            var config = _db.Queryable<SeafoodProductConfigAdminEditRecord>()
                .Where(c => c.productId == product.id)
                .First();

            var saleMode = config != null && IsSupported(config.saleMode)
                ? Normalize(config.saleMode)
                : ((goods.unit ?? string.Empty).Trim() == "条" ? Piece : WholeJin);
            var stockQty = config?.stockQty ?? Convert.ToDecimal(product.stock);
            var freezeQty = config?.freezeQty ?? 0m;

            return Success("获取成功", new
            {
                exists = config != null,
                goodsId,
                productId = product.id,
                isSeafoodCategory = goods.goodsCategoryId == SeafoodCategoryId,
                enabled = config?.enabled ?? false,
                saleMode,
                saleStep = GetStep(saleMode),
                unit = GetUnit(saleMode),
                stockQty,
                freezeQty,
                availableQty = Math.Max(0m, stockQty - freezeQty)
            });
        }

        /// <summary>
        /// 保存鲜鱼设置。存在冻结库存时禁止关闭或切换售卖方式。
        /// </summary>
        [HttpPost]
        public AdminUiCallBack Save([FromBody] SeafoodProductConfigSaveRequest entity)
        {
            if (entity == null || entity.goodsId <= 0)
            {
                return Fail("鲜鱼设置数据无效");
            }

            var saleMode = Normalize(entity.saleMode);
            if (!IsSupported(saleMode))
            {
                return Fail("售卖方式无效，请选择整斤、半斤或按条");
            }

            if (entity.stockQty < 0m || entity.stockQty > 999999m)
            {
                return Fail("鲜鱼库存必须在0到999999之间");
            }

            var step = GetStep(saleMode);
            if (!IsAligned(entity.stockQty, step))
            {
                return Fail(saleMode == HalfJin
                    ? "半斤商品库存必须按0.5递增"
                    : saleMode == Piece
                        ? "按条商品库存必须是整数条"
                        : "整斤商品库存必须是整数斤");
            }

            var goods = _db.Queryable<CoreCmsGoods>()
                .Where(g => g.id == entity.goodsId && g.isDel == false)
                .First();
            if (goods == null)
            {
                return Fail("商品不存在");
            }

            if (entity.enabled && goods.goodsCategoryId != SeafoodCategoryId)
            {
                return Fail("只有水产海鲜分类商品才能启用今日鲜鱼");
            }

            var product = _db.Queryable<CoreCmsProducts>()
                .Where(p => p.goodsId == entity.goodsId && p.isDel == false && p.isDefalut == true)
                .OrderBy(p => p.id, OrderByType.Asc)
                .First();
            if (product == null)
            {
                return Fail("商品缺少默认货品，无法保存鲜鱼设置");
            }

            var existing = _db.Queryable<SeafoodProductConfigAdminEditRecord>()
                .Where(c => c.productId == product.id)
                .First();
            var freezeQty = existing?.freezeQty ?? 0m;

            if (entity.stockQty < freezeQty)
            {
                return Fail($"当前已有{freezeQty:0.####}{GetUnit(existing?.saleMode)}冻结库存，总库存不能低于冻结数量");
            }

            if (freezeQty > 0m && existing != null)
            {
                if (!entity.enabled)
                {
                    return Fail("当前还有顾客留货，不能关闭今日鲜鱼");
                }

                if (!string.Equals(Normalize(existing.saleMode), saleMode, StringComparison.OrdinalIgnoreCase))
                {
                    return Fail("当前还有顾客留货，不能切换售卖方式");
                }
            }

            var now = DateTime.Now;

            try
            {
                _unitOfWork.BeginTran();

                if (existing == null)
                {
                    var inserted = _db.Insertable(new SeafoodProductConfigAdminEditRecord
                    {
                        productId = product.id,
                        enabled = entity.enabled,
                        saleMode = saleMode,
                        stockQty = entity.stockQty,
                        freezeQty = 0m,
                        createdAt = now,
                        updatedAt = now
                    }).ExecuteCommand();

                    if (inserted != 1)
                    {
                        throw new InvalidOperationException("鲜鱼设置保存失败");
                    }
                }
                else
                {
                    var affected = _db.Ado.ExecuteCommand(
                        "UPDATE SeafoodProductConfig " +
                        "SET enabled=@enabled, saleMode=@saleMode, stockQty=@stockQty, updatedAt=@now " +
                        "WHERE productId=@productId",
                        new SugarParameter("@enabled", entity.enabled),
                        new SugarParameter("@saleMode", saleMode),
                        new SugarParameter("@stockQty", entity.stockQty),
                        new SugarParameter("@now", now),
                        new SugarParameter("@productId", product.id));

                    if (affected != 1)
                    {
                        throw new InvalidOperationException("鲜鱼设置保存失败");
                    }
                }

                if (entity.enabled)
                {
                    var unitAffected = _db.Ado.ExecuteCommand(
                        "UPDATE CoreCmsGoods SET unit=@unit WHERE id=@goodsId AND isDel=0",
                        new SugarParameter("@unit", GetUnit(saleMode)),
                        new SugarParameter("@goodsId", entity.goodsId));
                    if (unitAffected != 1)
                    {
                        throw new InvalidOperationException("商品单位同步失败");
                    }
                }

                _unitOfWork.CommitTran();
            }
            catch (Exception ex)
            {
                try { _unitOfWork.RollbackTran(); } catch { }
                return Fail(ex is InvalidOperationException
                    ? ex.Message
                    : "鲜鱼设置保存失败，请稍后重试");
            }

            return Success("鲜鱼售卖设置已保存", new
            {
                goodsId = entity.goodsId,
                productId = product.id,
                enabled = entity.enabled,
                saleMode,
                saleStep = step,
                unit = GetUnit(saleMode),
                stockQty = entity.stockQty,
                freezeQty,
                availableQty = Math.Max(0m, entity.stockQty - freezeQty)
            });
        }

        /// <summary>
        /// 今日鲜鱼快速设置列表。
        /// 仅返回当前仍在商城上架的水产海鲜商品，
        /// 避免把已经退役的测试商品带入日常操作页面。
        /// </summary>
        [HttpPost]
        public AdminUiCallBack GetQuickList()
        {
            var goodsList = _db.Queryable<CoreCmsGoods>()
                .Where(g =>
                    g.goodsCategoryId == SeafoodCategoryId
                    && g.isDel == false
                    && g.isMarketable == true)
                .OrderBy(g => g.sort, OrderByType.Asc)
                .ToList();

            var rows = new List<object>();

            foreach (var goods in goodsList)
            {
                var product = _db.Queryable<CoreCmsProducts>()
                    .Where(p =>
                        p.goodsId == goods.id
                        && p.isDel == false
                        && p.isDefalut == true)
                    .OrderBy(p => p.id, OrderByType.Asc)
                    .First();

                if (product == null)
                {
                    continue;
                }

                var config = _db.Queryable<SeafoodProductConfigAdminEditRecord>()
                    .Where(c => c.productId == product.id)
                    .First();

                var saleMode =
                    config != null && IsSupported(config.saleMode)
                        ? Normalize(config.saleMode)
                        : ((goods.unit ?? string.Empty).Trim() == "条"
                            ? Piece
                            : WholeJin);

                var stockQty =
                    config?.stockQty
                    ?? Convert.ToDecimal(product.stock);

                var freezeQty =
                    config?.freezeQty
                    ?? 0m;

                rows.Add(new
                {
                    goodsId = goods.id,
                    productId = product.id,
                    name = goods.name,
                    enabled = config?.enabled ?? false,
                    saleMode,
                    saleStep = GetStep(saleMode),
                    unit = GetUnit(saleMode),
                    stockQty,
                    freezeQty,
                    availableQty = Math.Max(
                        0m,
                        stockQty - freezeQty
                    ),
                    price = product.price,
                    mktprice = product.mktprice
                });
            }

            return Success("获取成功", rows);
        }

        /// <summary>
        /// 今日鲜鱼快速保存。
        /// 仅修改鲜鱼售卖配置、商品单位及默认货品价格。
        /// 不修改图片、详情、SKU货号，也不修改CoreShop旧整数库存。
        /// </summary>
        [HttpPost]
        public AdminUiCallBack QuickSave(
            [FromBody] SeafoodProductQuickSaveRequest entity)
        {
            if (entity == null || entity.goodsId <= 0)
            {
                return Fail("鲜鱼设置数据无效");
            }

            var saleMode = Normalize(entity.saleMode);

            if (!IsSupported(saleMode))
            {
                return Fail(
                    "售卖方式无效，请选择整斤、半斤或按条"
                );
            }

            if (
                entity.stockQty < 0m
                || entity.stockQty > 999999m
            )
            {
                return Fail(
                    "鲜鱼库存必须在0到999999之间"
                );
            }

            var step = GetStep(saleMode);

            if (!IsAligned(entity.stockQty, step))
            {
                return Fail(
                    saleMode == HalfJin
                        ? "半斤商品库存必须按0.5递增"
                        : saleMode == Piece
                            ? "按条商品库存必须是整数条"
                            : "整斤商品库存必须是整数斤"
                );
            }

            if (
                entity.price < 0m
                || entity.price > 999999m
                || entity.mktprice < 0m
                || entity.mktprice > 999999m
            )
            {
                return Fail(
                    "商品价格必须在0到999999之间"
                );
            }

            if (entity.mktprice < entity.price)
            {
                return Fail(
                    "非会员价不能低于会员价"
                );
            }

            var goods = _db.Queryable<CoreCmsGoods>()
                .Where(g =>
                    g.id == entity.goodsId
                    && g.isDel == false)
                .First();

            if (goods == null)
            {
                return Fail("商品不存在");
            }

            if (
                goods.goodsCategoryId
                != SeafoodCategoryId
            )
            {
                return Fail(
                    "只有水产海鲜分类商品才能使用鲜鱼快速设置"
                );
            }

            var product = _db.Queryable<CoreCmsProducts>()
                .Where(p =>
                    p.goodsId == entity.goodsId
                    && p.isDel == false
                    && p.isDefalut == true)
                .OrderBy(p => p.id, OrderByType.Asc)
                .First();

            if (product == null)
            {
                return Fail(
                    "商品缺少默认货品，无法保存鲜鱼设置"
                );
            }

            var existing =
                _db.Queryable<SeafoodProductConfigAdminEditRecord>()
                    .Where(c => c.productId == product.id)
                    .First();

            var freezeQty =
                existing?.freezeQty
                ?? 0m;

            if (entity.stockQty < freezeQty)
            {
                return Fail(
                    $"当前已有{freezeQty:0.####}" +
                    $"{GetUnit(existing?.saleMode)}冻结库存，" +
                    "总库存不能低于冻结数量"
                );
            }

            if (freezeQty > 0m && existing != null)
            {
                if (!entity.enabled)
                {
                    return Fail(
                        "当前还有顾客留货，不能关闭今日鲜鱼"
                    );
                }

                if (
                    !string.Equals(
                        Normalize(existing.saleMode),
                        saleMode,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return Fail(
                        "当前还有顾客留货，不能切换售卖方式"
                    );
                }
            }

            var now = DateTime.Now;

            try
            {
                _unitOfWork.BeginTran();

                if (existing == null)
                {
                    var inserted = _db.Insertable(
                        new SeafoodProductConfigAdminEditRecord
                        {
                            productId = product.id,
                            enabled = entity.enabled,
                            saleMode = saleMode,
                            stockQty = entity.stockQty,
                            freezeQty = 0m,
                            createdAt = now,
                            updatedAt = now
                        }
                    ).ExecuteCommand();

                    if (inserted != 1)
                    {
                        throw new InvalidOperationException(
                            "鲜鱼设置保存失败"
                        );
                    }
                }
                else
                {
                    var configAffected =
                        _db.Ado.ExecuteCommand(
                            "UPDATE SeafoodProductConfig " +
                            "SET enabled=@enabled, " +
                            "saleMode=@saleMode, " +
                            "stockQty=@stockQty, " +
                            "updatedAt=@now " +
                            "WHERE productId=@productId",
                            new SugarParameter(
                                "@enabled",
                                entity.enabled
                            ),
                            new SugarParameter(
                                "@saleMode",
                                saleMode
                            ),
                            new SugarParameter(
                                "@stockQty",
                                entity.stockQty
                            ),
                            new SugarParameter(
                                "@now",
                                now
                            ),
                            new SugarParameter(
                                "@productId",
                                product.id
                            )
                        );

                    if (configAffected != 1)
                    {
                        throw new InvalidOperationException(
                            "鲜鱼设置保存失败"
                        );
                    }
                }

                var productPriceAffected =
                    _db.Ado.ExecuteCommand(
                        "UPDATE CoreCmsProducts " +
                        "SET price=@price, " +
                        "mktprice=@mktprice " +
                        "WHERE id=@productId AND isDel=0",
                        new SugarParameter(
                            "@price",
                            entity.price
                        ),
                        new SugarParameter(
                            "@mktprice",
                            entity.mktprice
                        ),
                        new SugarParameter(
                            "@productId",
                            product.id
                        )
                    );

                if (productPriceAffected > 1)
                {
                    throw new InvalidOperationException(
                        "默认货品价格同步结果异常"
                    );
                }

                if (entity.enabled)
                {
                    var unitAffected =
                        _db.Ado.ExecuteCommand(
                            "UPDATE CoreCmsGoods " +
                            "SET unit=@unit " +
                            "WHERE id=@goodsId AND isDel=0",
                            new SugarParameter(
                                "@unit",
                                GetUnit(saleMode)
                            ),
                            new SugarParameter(
                                "@goodsId",
                                entity.goodsId
                            )
                        );

                    if (unitAffected > 1)
                    {
                        throw new InvalidOperationException(
                            "商品单位同步结果异常"
                        );
                    }
                }

                _unitOfWork.CommitTran();
            }
            catch (Exception ex)
            {
                try
                {
                    _unitOfWork.RollbackTran();
                }
                catch
                {
                }

                Console.Error.WriteLine(
                    "Seafood QuickSave failed: " + ex
                );

                return Fail(
                    ex is InvalidOperationException
                        ? ex.Message
                        : "鲜鱼快速设置保存失败，请稍后重试"
                );
            }

            return Success(
                "鲜鱼设置已保存",
                new
                {
                    goodsId = entity.goodsId,
                    productId = product.id,
                    enabled = entity.enabled,
                    saleMode,
                    saleStep = step,
                    unit = GetUnit(saleMode),
                    stockQty = entity.stockQty,
                    freezeQty,
                    availableQty = Math.Max(
                        0m,
                        entity.stockQty - freezeQty
                    ),
                    price = entity.price,
                    mktprice = entity.mktprice
                }
            );
        }

        /// <summary>
        /// 新商品创建成功后，使用本次提交的唯一默认SKU货号
        /// 精确定位商品，再保存鲜鱼设置。
        /// 不使用“最新商品”之类存在并发风险的定位方式。
        /// </summary>
        [HttpPost]
        public AdminUiCallBack SaveForNewProduct(
            [FromBody] SeafoodProductConfigCreateRequest entity)
        {
            if (
                entity == null
                || string.IsNullOrWhiteSpace(entity.productSn)
            )
            {
                return Fail("新商品默认货品货号不能为空");
            }

            var productSn = entity.productSn.Trim();

            var products = _db.Queryable<CoreCmsProducts>()
                .Where(p =>
                    p.sn == productSn
                    && p.isDel == false
                    && p.isDefalut == true)
                .OrderBy(p => p.id, OrderByType.Asc)
                .Take(2)
                .ToList();

            if (products.Count == 0)
            {
                return Fail(
                    "商品已经创建，但未找到本次创建的默认货品"
                );
            }

            if (products.Count != 1)
            {
                return Fail(
                    "默认货品货号定位结果异常，为安全起见未保存鲜鱼设置"
                );
            }

            var product = products[0];

            return Save(new SeafoodProductConfigSaveRequest
            {
                goodsId = product.goodsId,
                enabled = entity.enabled,
                saleMode = entity.saleMode,
                stockQty = entity.stockQty
            });
        }

        private static string Normalize(string value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static bool IsSupported(string value)
        {
            var mode = Normalize(value);
            return mode == WholeJin || mode == HalfJin || mode == Piece;
        }

        private static decimal GetStep(string saleMode)
        {
            return Normalize(saleMode) == HalfJin ? 0.5m : 1m;
        }

        private static string GetUnit(string saleMode)
        {
            return Normalize(saleMode) == Piece ? "条" : "斤";
        }

        private static bool IsAligned(decimal quantity, decimal step)
        {
            if (step <= 0m) return false;
            var units = quantity / step;
            return units == decimal.Truncate(units);
        }

        private static AdminUiCallBack Success(string message, object data)
        {
            return new AdminUiCallBack
            {
                code = 0,
                msg = message,
                data = data
            };
        }

        private static AdminUiCallBack Fail(string message)
        {
            return new AdminUiCallBack
            {
                code = 1,
                msg = message
            };
        }
    }

    public class SeafoodProductQuickSaveRequest
    {
        public int goodsId { get; set; }
        public bool enabled { get; set; }
        public string saleMode { get; set; }
        public decimal stockQty { get; set; }
        public decimal price { get; set; }
        public decimal mktprice { get; set; }
    }

    public class SeafoodProductConfigCreateRequest
    {
        public string productSn { get; set; }
        public bool enabled { get; set; }
        public string saleMode { get; set; }
        public decimal stockQty { get; set; }
    }

    public class SeafoodProductConfigSaveRequest
    {
        public int goodsId { get; set; }
        public bool enabled { get; set; }
        public string saleMode { get; set; }
        public decimal stockQty { get; set; }
    }

    [SugarTable("SeafoodProductConfig")]
    internal class SeafoodProductConfigAdminEditRecord
    {
        [SugarColumn(IsPrimaryKey = true)]
        public int productId { get; set; }
        public bool enabled { get; set; }

        [SugarColumn(Length = 20)]
        public string saleMode { get; set; }

        public decimal stockQty { get; set; }
        public decimal freezeQty { get; set; }
        public DateTime createdAt { get; set; }
        public DateTime updatedAt { get; set; }
    }
}
