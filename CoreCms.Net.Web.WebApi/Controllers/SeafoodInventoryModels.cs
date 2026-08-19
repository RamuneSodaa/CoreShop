using System;
using SqlSugar;

namespace CoreCms.Net.Web.WebApi.Controllers
{
    internal static class SeafoodSaleModes
    {
        public const string WholeJin = "whole_jin";
        public const string HalfJin = "half_jin";
        public const string Piece = "piece";

        public static bool IsSupported(string saleMode)
        {
            return saleMode == WholeJin || saleMode == HalfJin || saleMode == Piece;
        }

        public static decimal GetStep(string saleMode)
        {
            return saleMode == HalfJin ? 0.5m : 1m;
        }

        public static string GetUnit(string saleMode)
        {
            return saleMode == Piece ? "条" : "斤";
        }
    }

    [SugarTable("SeafoodProductConfig")]
    internal class SeafoodProductConfigRecord
    {
        [SugarColumn(IsPrimaryKey = true)]
        public int productId { get; set; }

        public bool enabled { get; set; }

        [SugarColumn(Length = 20)]
        public string saleMode { get; set; }

        [SugarColumn(ColumnDataType = "decimal(18,4)")]
        public decimal stockQty { get; set; }

        [SugarColumn(ColumnDataType = "decimal(18,4)")]
        public decimal freezeQty { get; set; }

        public DateTime createdAt { get; set; }
        public DateTime updatedAt { get; set; }
    }
}
