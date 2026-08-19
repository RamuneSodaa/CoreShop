/**
 *  全局配置文件
 *  @version 1.0.0
 */

// 开发H5直连本机WebApi。
// 正式H5使用当前访问站点作为API根地址，
// 由生产反向代理把 /api 请求转发给WebApi。
let apiBaseUrl = 'http://127.0.0.1:2015';

// #ifdef H5
const h5Host = window.location.hostname;
const isLocalH5 =
    h5Host === 'localhost'
    || h5Host === '127.0.0.1';

if (!isLocalH5) {
    apiBaseUrl = window.location.origin;
}
// #endif

export { apiBaseUrl };

// 本地上传图片由Admin静态目录提供。
// 正式鲜鱼图片由Catalog按照Admin AppUrl返回。
export const apiFilesUrl = 'http://127.0.0.1:1987';

// #ifdef H5
export const baseUrl =
    process.env.NODE_ENV === 'development'
        ? window.location.origin + '/'
        : apiBaseUrl
// #endif

export const paymentType = {
    //支付单类型
    order: 1, //订单
    recharge: 2, //充值
    formPay: 3, //表单订单
    formOrder: 4, //表单付款码
    serviceOrder: 5, //服务订单
};

//nav页面导航类型
export const navLinkType = {
    urlLink: 1, //"URL链接"
    shop: 2,// "商品"
    article: 3,// "文章"
    articleCategory: 4,// "文章分类",
    intelligentForms: 5// "智能表单"
};
