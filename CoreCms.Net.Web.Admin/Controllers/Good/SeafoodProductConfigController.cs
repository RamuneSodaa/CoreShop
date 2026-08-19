using System;
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
