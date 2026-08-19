using System;
using System.Collections.Generic;
using System.Linq;
using CoreCms.Net.IRepository.UnitOfWork;
using CoreCms.Net.Model.Entities;
using CoreCms.Net.Model.ViewModels.UI;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;

namespace CoreCms.Net.Web.WebApi.Controllers
{
    /// <summary>
    /// 鲜鱼接龙预订接口。
    /// 不要求商城登录，不处理在线支付；创建预订时原子冻结可售库存。
    /// </summary>
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class SeafoodReservationController : ControllerBase
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly SqlSugarScope _db;

        public SeafoodReservationController(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
            _db = unitOfWork.GetDbClient();
            EnsureTables();
        }

        /// <summary>
        /// 创建鲜鱼预订。不要求 Token。
        /// </summary>
        [HttpPost]
        public WebApiCallBack Create([FromBody] SeafoodReservationCreateRequest entity)
        {
            var jm = new WebApiCallBack();

            if (entity == null)
            {
                jm.msg = "预订数据不能为空";
                return jm;
            }

            var customerName = (entity.customerName ?? string.Empty).Trim();
            if (customerName.Length < 1 || customerName.Length > 40)
            {
                jm.msg = "请输入1-40字的称呼或微信名";
                return jm;
            }

            var deliveryType = (entity.deliveryType ?? string.Empty).Trim().ToLowerInvariant();
            if (deliveryType != "pickup" && deliveryType != "shipping")
            {
                jm.msg = "请选择到店取或邮寄";
                return jm;
            }

            var contact = (entity.contact ?? string.Empty).Trim();
            var note = (entity.note ?? string.Empty).Trim();
            if (contact.Length > 80)
            {
                jm.msg = "联系方式不能超过80字";
                return jm;
            }
            if (note.Length > 500)
            {
                jm.msg = "备注不能超过500字";
                return jm;
            }

            var sourceItems = entity.items ?? new List<SeafoodReservationCreateItem>();
            var items = sourceItems
                .Where(x => x != null && x.productId > 0 && x.quantity > 0)
                .GroupBy(x => x.productId)
                .Select(g => new SeafoodReservationCreateItem
                {
                    productId = g.Key,
                    quantity = g.Sum(x => x.quantity)
                })
                .ToList();

            if (items.Count == 0)
            {
                jm.msg = "请选择要预订的鱼货";
                return jm;
            }
            if (items.Count > 20 || items.Any(x => x.quantity > 999))
            {
                jm.msg = "本次预订数量异常，请重新选择";
                return jm;
            }

            var requestId = (entity.requestId ?? string.Empty).Trim();
            if (requestId.Length > 64)
            {
                jm.msg = "请求标识异常";
                return jm;
            }

            // 前端重复点击/网络重试时，尽量返回同一笔预订，避免重复占库存。
            if (!string.IsNullOrEmpty(requestId))
            {
                var existing = _db.Queryable<SeafoodReservationRecord>()
                    .Where(x => x.requestId == requestId)
                    .OrderBy(x => x.id, OrderByType.Desc)
                    .First();
                if (existing != null)
                {
                    jm.status = true;
                    jm.msg = "预订已提交";
                    jm.data = BuildReservationResult(existing);
                    return jm;
                }
            }

            var reservationNo = "YR" + DateTime.Now.ToString("yyyyMMddHHmmssfff") + Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant();
            var lookupToken = Guid.NewGuid().ToString("N");
            var now = DateTime.Now;
            decimal totalAmount = 0m;
            var reservationItems = new List<SeafoodReservationItemRecord>();

            try
            {
                _unitOfWork.BeginTran();

                foreach (var requested in items)
                {
                    var product = _db.Queryable<CoreCmsProducts>()
                        .Where(p => p.id == requested.productId && p.isDel == false && p.marketable == true)
                        .First();

                    if (product == null)
                    {
                        throw new InvalidOperationException("有鱼货已下架，请刷新后重新选择");
                    }

                    var goods = _db.Queryable<CoreCmsGoods>()
                        .Where(g => g.id == product.goodsId && g.isDel == false && g.isMarketable == true)
                        .First();

                    if (goods == null)
                    {
                        throw new InvalidOperationException("有鱼货已下架，请刷新后重新选择");
                    }

                    // 条件 UPDATE 在数据库端一次完成“检查可售库存 + 冻结库存”，
                    // 多人同时提交时只有仍满足 stock-freezeStock >= quantity 的请求能成功。
                    var affected = _db.Ado.ExecuteCommand(
                        "UPDATE CoreCmsProducts " +
                        "SET freezeStock = freezeStock + @qty " +
                        "WHERE id = @productId AND isDel = 0 AND marketable = 1 " +
                        "AND (stock - freezeStock) >= @qty",
                        new SugarParameter("@qty", requested.quantity),
                        new SugarParameter("@productId", requested.productId));

                    if (affected != 1)
                    {
                        throw new InvalidOperationException($"{goods.name}库存不足，请刷新后重新选择");
                    }

                    var unitPrice = product.price;
                    var lineAmount = unitPrice * requested.quantity;
                    totalAmount += lineAmount;

                    reservationItems.Add(new SeafoodReservationItemRecord
                    {
                        productId = product.id,
                        goodsId = Convert.ToInt32(product.goodsId),
                        goodsName = goods.name ?? string.Empty,
                        unit = string.IsNullOrEmpty(goods.unit) ? "斤" : goods.unit,
                        quantity = requested.quantity,
                        unitPrice = unitPrice,
                        amount = lineAmount,
                        image = goods.image ?? string.Empty,
                        createdAt = now
                    });
                }

                var reservation = new SeafoodReservationRecord
                {
                    reservationNo = reservationNo,
                    lookupToken = lookupToken,
                    requestId = requestId,
                    customerName = customerName,
                    contact = contact,
                    deliveryType = deliveryType,
                    note = note,
                    totalAmount = totalAmount,
                    status = 0,
                    createdAt = now,
                    updatedAt = now
                };

                var reservationId = _db.Insertable(reservation).ExecuteReturnBigIdentity();
                if (reservationId <= 0)
                {
                    throw new InvalidOperationException("预订记录保存失败");
                }

                foreach (var item in reservationItems)
                {
                    item.reservationId = reservationId;
                }

                var inserted = _db.Insertable(reservationItems).ExecuteCommand();
                if (inserted != reservationItems.Count)
                {
                    throw new InvalidOperationException("预订明细保存失败");
                }

                reservation.id = reservationId;
                _unitOfWork.CommitTran();

                jm.status = true;
                jm.msg = "预订成功";
                jm.data = new
                {
                    reservationNo,
                    lookupToken,
                    customerName,
                    deliveryType,
                    totalAmount,
                    itemCount = reservationItems.Count,
                    items = reservationItems.Select(x => new
                    {
                        x.productId,
                        x.goodsName,
                        x.unit,
                        x.quantity,
                        x.unitPrice,
                        x.amount
                    }).ToList()
                };
                return jm;
            }
            catch (Exception ex)
            {
                try { _unitOfWork.RollbackTran(); } catch { }
                jm.status = false;
                jm.msg = ex is InvalidOperationException ? ex.Message : "预订提交失败，请稍后重试";
                return jm;
            }
        }

        /// <summary>
        /// 使用创建预订时返回的随机 lookupToken 查询单笔预订。不要求商城登录。
        /// </summary>
        [HttpPost]
        public WebApiCallBack Get([FromBody] SeafoodReservationLookupRequest entity)
        {
            var jm = new WebApiCallBack();
            var token = (entity?.lookupToken ?? string.Empty).Trim();
            if (token.Length < 20)
            {
                jm.msg = "预订凭证无效";
                return jm;
            }

            var reservation = _db.Queryable<SeafoodReservationRecord>()
                .Where(x => x.lookupToken == token)
                .First();
            if (reservation == null)
            {
                jm.msg = "未找到预订记录";
                return jm;
            }

            var items = _db.Queryable<SeafoodReservationItemRecord>()
                .Where(x => x.reservationId == reservation.id)
                .OrderBy(x => x.id)
                .ToList();

            jm.status = true;
            jm.msg = "获取成功";
            jm.data = new
            {
                reservation.reservationNo,
                reservation.customerName,
                reservation.contact,
                reservation.deliveryType,
                reservation.note,
                reservation.totalAmount,
                reservation.status,
                reservation.createdAt,
                items
            };
            return jm;
        }

        private object BuildReservationResult(SeafoodReservationRecord reservation)
        {
            return new
            {
                reservation.reservationNo,
                reservation.lookupToken,
                reservation.customerName,
                reservation.deliveryType,
                reservation.totalAmount
            };
        }

        private void EnsureTables()
        {
            _db.CodeFirst.InitTables<SeafoodReservationRecord>();
            _db.CodeFirst.InitTables<SeafoodReservationItemRecord>();
        }
    }

    public class SeafoodReservationCreateRequest
    {
        public string customerName { get; set; }
        public string contact { get; set; }
        public string deliveryType { get; set; }
        public string note { get; set; }
        public string requestId { get; set; }
        public List<SeafoodReservationCreateItem> items { get; set; }
    }

    public class SeafoodReservationCreateItem
    {
        public int productId { get; set; }
        public int quantity { get; set; }
    }

    public class SeafoodReservationLookupRequest
    {
        public string lookupToken { get; set; }
    }

    [SugarTable("SeafoodReservation")]
    public class SeafoodReservationRecord
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public long id { get; set; }

        [SugarColumn(Length = 40)]
        public string reservationNo { get; set; }

        [SugarColumn(Length = 64)]
        public string lookupToken { get; set; }

        [SugarColumn(Length = 64, IsNullable = true)]
        public string requestId { get; set; }

        [SugarColumn(Length = 80)]
        public string customerName { get; set; }

        [SugarColumn(Length = 120, IsNullable = true)]
        public string contact { get; set; }

        [SugarColumn(Length = 20)]
        public string deliveryType { get; set; }

        [SugarColumn(Length = 500, IsNullable = true)]
        public string note { get; set; }

        public decimal totalAmount { get; set; }
        public int status { get; set; }
        public DateTime createdAt { get; set; }
        public DateTime updatedAt { get; set; }
    }

    [SugarTable("SeafoodReservationItem")]
    public class SeafoodReservationItemRecord
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public long id { get; set; }
        public long reservationId { get; set; }
        public int productId { get; set; }
        public int goodsId { get; set; }

        [SugarColumn(Length = 255)]
        public string goodsName { get; set; }

        [SugarColumn(Length = 30)]
        public string unit { get; set; }

        public int quantity { get; set; }
        public decimal unitPrice { get; set; }
        public decimal amount { get; set; }

        [SugarColumn(Length = 500, IsNullable = true)]
        public string image { get; set; }

        public DateTime createdAt { get; set; }
    }
}
