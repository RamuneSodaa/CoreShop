/**
 *  全局配置文件
 *  @version 1.0.0
 */

// 本地卖鱼接龙 MVP：接口指向本机 CoreShop WebApi。
// 正式部署时再替换为生产 API 域名。
export const apiBaseUrl = 'http://127.0.0.1:2015';
// 本地上传图片由 Admin 静态目录提供。
export const apiFilesUrl = 'http://127.0.0.1:1987';

// #ifdef H5
export const baseUrl = process.env.NODE_ENV === 'development' ? window.location.origin + '/' : apiBaseUrl
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
