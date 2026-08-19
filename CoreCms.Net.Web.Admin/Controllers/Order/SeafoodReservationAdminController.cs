using System;
using System.ComponentModel;
using System.Linq;
using CoreCms.Net.IRepository.UnitOfWork;
using CoreCms.Net.Model.ViewModels.UI;
using CoreCms.Net.Web.Admin.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;

namespace CoreCms.Net.Web.Admin.Controllers
{
    /// <summary>
    /// 鲜鱼预订后台管理。
    /// </summary>
    [Description("鲜鱼预订管理")]
    [Route("api/[controller]/[action]")]
    [ApiController]
    [RequiredErrorForAdmin]
    [Authorize]
    public class SeafoodReservationAdminController : ControllerBase
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly SqlSugarScope _db;

        /// <summary>
        /// 构造函数。
        /// </summary>
        public SeafoodReservationAdminController(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
            _db = unitOfWork.GetDbClient();
            EnsureTables();
        }

        /// <summary>
        /// 获取预订状态统计。
        /// </summary>
        [HttpPost]
        public AdminUiCallBack GetIndex()
        {
            var jm = new AdminUiCallBack
            {
                code = 0,
                msg = "获取成功",
                data = new
                {
                    all = _db.Queryable<SeafoodReservationAdminRecord>().Count(),
                    pending = _db.Queryable<SeafoodReservationAdminRecord>().Where(x => x.status == 0).Count(),
                    confirmed = _db.Queryable<SeafoodReservationAdminRecord>().Where(x => x.status == 1).Count(),
                    completed = _db.Queryable<SeafoodReservationAdminRecord>().Where(x => x.status == 2).Count(),
                    cancelled = _db.Queryable<SeafoodReservationAdminRecord>().Where(x => x.status == 3).Count()
                }
            };
            return jm;
        }

        /// <summary>
        /// 获取预订分页列表。
        /// </summary>
        [HttpPost]
        public AdminUiCallBack GetPageList()
        {
            var jm = new AdminUiCallBack();
            var pageCurrent = ParsePositiveInt(Request.Form["page"].FirstOrDefault(), 1);
            var pageSize = ParsePositiveInt(Request.Form["limit"].FirstOrDefault(), 30);

            var status = -1;
            var statusRaw = Request.Form["status"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(statusRaw) && int.TryParse(statusRaw, out var parsedStatus))
            {
                status = parsedStatus;
            }

            var keyword = (Request.Form["keyword"].FirstOrDefault() ?? string.Empty).Trim();
            var deliveryType = (Request.Form["deliveryType"].FirstOrDefault() ?? string.Empty).Trim();

            var query = _db.Queryable<SeafoodReservationAdminRecord>();

            if (status >= 0 && status <= 3)
            {
                query = query.Where(x => x.status == status);
            }

            if (!string.IsNullOrEmpty(deliveryType))
            {
                query = query.Where(x => x.deliveryType == deliveryType);
            }

            if (!string.IsNullOrEmpty(keyword))
            {
                query = query.Where(x =>
                    x.reservationNo.Contains(keyword) ||
                    x.customerName.Contains(keyword) ||
                    x.contact.Contains(keyword));
            }

            var total = 0;
            var reservations = query
                .OrderBy(x => x.id, OrderByType.Desc)
                .ToPageList(pageCurrent, pageSize, ref total);

            var rows = reservations.Select(x =>
            {
                var items = _db.Queryable<SeafoodReservationItemAdminRecord>()
                    .Where(i => i.reservationId == x.id)
                    .OrderBy(i => i.id)
                    .ToList();

                var totalQuantity = items.Sum(i => i.quantity);
                var goodsSummary = string.Join("、", items.Select(i =>
                    $"{i.goodsName} {i.quantity}{(string.IsNullOrWhiteSpace(i.unit) ? "斤" : i.unit)}"));

                return new
                {
                    x.id,
                    x.reservationNo,
                    x.customerName,
                    x.contact,
                    x.deliveryType,
                    x.note,
                    x.totalAmount,
                    x.status,
                    totalQuantity,
                    goodsSummary,
                    createdAt = x.createdAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    updatedAt = x.updatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    items = items.Select(i => new
                    {
                        i.id,
                        i.productId,
                        i.goodsId,
                        i.goodsName,
                        i.unit,
                        i.quantity,
                        i.unitPrice,
                        i.amount,
                        i.image
                    }).ToList()
                };
            }).ToList();

            jm.code = 0;
            jm.msg = "获取成功";
            jm.count = total;
            jm.data = rows;
            return jm;
        }

        /// <summary>
        /// 获取单笔预订详情。
        /// </summary>
        [HttpPost]
        public AdminUiCallBack GetDetail()
        {
            var jm = new AdminUiCallBack();
            if (!TryGetId(out var id))
            {
                jm.msg = "预订ID无效";
                return jm;
            }

            var reservation = _db.Queryable<SeafoodReservationAdminRecord>()
                .Where(x => x.id == id)
                .First();

            if (reservation == null)
            {
                jm.msg = "未找到预订记录";
                return jm;
            }

            var items = _db.Queryable<SeafoodReservationItemAdminRecord>()
                .Where(x => x.reservationId == id)
                .OrderBy(x => x.id)
                .ToList();

            jm.code = 0;
            jm.msg = "获取成功";
            jm.data = new
            {
                reservation.id,
                reservation.reservationNo,
                reservation.customerName,
                reservation.contact,
                reservation.deliveryType,
                reservation.note,
                reservation.totalAmount,
                reservation.status,
                createdAt = reservation.createdAt.ToString("yyyy-MM-dd HH:mm:ss"),
                updatedAt = reservation.updatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                items
            };
            return jm;
        }

        /// <summary>
        /// 确认预订：待处理 -> 已确认。库存保持冻结。
        /// </summary>
        [HttpPost]
        public AdminUiCallBack Confirm()
        {
            if (!TryGetId(out var id))
            {
                return Fail("预订ID无效");
            }

            var affected = _db.Ado.ExecuteCommand(
                "UPDATE SeafoodReservation " +
                "SET status = 1, updatedAt = @now " +
                "WHERE id = @id AND status = 0",
                new SugarParameter("@now", DateTime.Now),
                new SugarParameter("@id", id));

            return affected == 1
                ? Success("预订已确认，库存继续为顾客留货")
                : Fail("只能确认‘待处理’的预订，请刷新后重试");
        }

        /// <summary>
        /// 取消预订：释放此前冻结的库存。
        /// </summary>
        [HttpPost]
        public AdminUiCallBack Cancel()
        {
            if (!TryGetId(out var id))
            {
                return Fail("预订ID无效");
            }

            try
            {
                _unitOfWork.BeginTran();

                // 先原子抢占状态迁移权。并发取消/完成只能有一个请求成功。
                var reservationAffected = _db.Ado.ExecuteCommand(
                    "UPDATE SeafoodReservation " +
                    "SET status = 3, updatedAt = @now " +
                    "WHERE id = @id AND status IN (0, 1)",
                    new SugarParameter("@now", DateTime.Now),
                    new SugarParameter("@id", id));

                if (reservationAffected != 1)
                {
                    throw new InvalidOperationException("只能取消‘待处理’或‘已确认’的预订");
                }

                var items = _db.Queryable<SeafoodReservationItemAdminRecord>()
                    .Where(x => x.reservationId == id)
                    .OrderBy(x => x.productId)
                    .ToList();

                if (items.Count == 0)
                {
                    throw new InvalidOperationException("预订明细不存在，已停止操作");
                }

                foreach (var item in items)
                {
                    var stockAffected = _db.Ado.ExecuteCommand(
                        "UPDATE CoreCmsProducts " +
                        "SET freezeStock = freezeStock - @qty " +
                        "WHERE id = @productId AND freezeStock >= @qty",
                        new SugarParameter("@qty", item.quantity),
                        new SugarParameter("@productId", item.productId));

                    if (stockAffected != 1)
                    {
                        throw new InvalidOperationException(
                            $"{item.goodsName}冻结库存异常，已回滚取消操作");
                    }
                }

                _unitOfWork.CommitTran();
                return Success("预订已取消，所占库存已自动释放");
            }
            catch (Exception ex)
            {
                try { _unitOfWork.RollbackTran(); } catch { }
                return Fail(ex is InvalidOperationException
                    ? ex.Message
                    : "取消预订失败，请稍后重试");
            }
        }

        /// <summary>
        /// 完成预订：扣减总库存并释放冻结库存。
        /// </summary>
        [HttpPost]
        public AdminUiCallBack Complete()
        {
            if (!TryGetId(out var id))
            {
                return Fail("预订ID无效");
            }

            try
            {
                _unitOfWork.BeginTran();

                // 同取消逻辑：先锁定状态迁移，避免同一预订被重复完成/取消。
                var reservationAffected = _db.Ado.ExecuteCommand(
                    "UPDATE SeafoodReservation " +
                    "SET status = 2, updatedAt = @now " +
                    "WHERE id = @id AND status IN (0, 1)",
                    new SugarParameter("@now", DateTime.Now),
                    new SugarParameter("@id", id));

                if (reservationAffected != 1)
                {
                    throw new InvalidOperationException("只能完成‘待处理’或‘已确认’的预订");
                }

                var items = _db.Queryable<SeafoodReservationItemAdminRecord>()
                    .Where(x => x.reservationId == id)
                    .OrderBy(x => x.productId)
                    .ToList();

                if (items.Count == 0)
                {
                    throw new InvalidOperationException("预订明细不存在，已停止操作");
                }

                foreach (var item in items)
                {
                    var stockAffected = _db.Ado.ExecuteCommand(
                        "UPDATE CoreCmsProducts " +
                        "SET stock = stock - @qty, freezeStock = freezeStock - @qty " +
                        "WHERE id = @productId AND stock >= @qty AND freezeStock >= @qty",
                        new SugarParameter("@qty", item.quantity),
                        new SugarParameter("@productId", item.productId));

                    if (stockAffected != 1)
                    {
                        throw new InvalidOperationException(
                            $"{item.goodsName}库存状态异常，已回滚完成操作");
                    }
                }

                _unitOfWork.CommitTran();
                return Success("预订已完成，总库存和冻结库存已同步结算");
            }
            catch (Exception ex)
            {
                try { _unitOfWork.RollbackTran(); } catch { }
                return Fail(ex is InvalidOperationException
                    ? ex.Message
                    : "完成预订失败，请稍后重试");
            }
        }

        private bool TryGetId(out long id)
        {
            var raw = Request.Form["id"].FirstOrDefault();
            return long.TryParse(raw, out id) && id > 0;
        }

        private static int ParsePositiveInt(string value, int fallback)
        {
            return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
        }

        private static AdminUiCallBack Success(string message)
        {
            return new AdminUiCallBack
            {
                code = 0,
                msg = message
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

        private void EnsureTables()
        {
            _db.CodeFirst.InitTables<SeafoodReservationAdminRecord>();
            _db.CodeFirst.InitTables<SeafoodReservationItemAdminRecord>();
        }
    }

    [SugarTable("SeafoodReservation")]
    internal class SeafoodReservationAdminRecord
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
    internal class SeafoodReservationItemAdminRecord
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
