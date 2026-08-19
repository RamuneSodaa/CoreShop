<template>
    <view>
        <u-toast ref="uToast" /><u-no-network></u-no-network>

        <template v-if="seafoodMode">
            <u-navbar title="我的预订"></u-navbar>
            <seafood-my-reservations ref="seafoodReservations"></seafood-my-reservations>
        </template>

        <template v-else>
            <u-navbar title="订单列表"></u-navbar>
            <view class="orderWrap">
                <view class="u-padding-0">
                    <u-subsection :list="tabs" :current="current" :animation="true" @change="onClickItem" active-color="#ff9900" mode="button"></u-subsection>
                </view>

                <view class="page-box" v-if="listData.length > 0">
                    <view class="orderList" v-for="(order, orderIndex) in listData" :key="orderIndex">
                        <view class="top" @click="goOrderDetail(order.orderId)">
                            <view class="left">
                                <u-icon name="home" :size="30" color="rgb(94,94,94)"></u-icon>
                                <view class="store">订单号 : {{order.orderId}}</view>
                                <u-icon name="arrow-right" color="rgb(203,203,203)" :size="26"></u-icon>
                            </view>
                            <view class="right">{{ order.orderStatusName }}</view>
                        </view>
                        <view class="item" v-for="(goods, indexGoods) in order.items" :key="indexGoods">
                            <view class="left"><image :src="goods.imageUrl && goods.imageUrl!='null' ? goods.imageUrl : '/static/images/common/empty-banner.png'" mode="aspectFill"></image></view>
                            <view class="content">
                                <view class="title u-line-2">{{goods.name}}</view>
                                <view class="type">{{goods.addon}}</view>
                                <view class="delivery-time">下单时间：{{ $u.timeFormat(goods.createTime, 'yyyy-mm-dd hh:MM:ss') }}</view>
                            </view>
                            <view class="right">
                                <view class="price">
                                    ￥{{ goods.price }}
                                </view>
                                <view class="number">x{{ goods.nums }}</view>
                            </view>
                        </view>
                        <view class="total">
                            共{{ order.items.length }}件商品 合计:
                            <text class="total-price">
                                ￥{{ order.orderAmount }}
                            </text>
                        </view>
                        <view class="bottom">
                            <view class="more">
                                <u-tag :text="order.typeText" mode="light" />
                            </view>
                            <view class="u-flex">
                                <view class='logistics coreshop-btn' @click="goOrderDetail(order.orderId)">查看详情</view>
                                <view class='coreshop-btn exchange' v-if="order.status === 1 && order.payStatus === 1" @click="goToPay(order.orderId)">立即支付</view>
                                <view class='coreshop-btn exchange' v-if="order.status === 1 && order.payStatus >= 2 && order.shipStatus >= 3 && order.confirmStatus === 1" @click="tackDelivery(current,orderIndex)">确认收货</view>
                                <view class='evaluate coreshop-btn' v-if="order.status === 1 && order.payStatus >= 2 && order.shipStatus >= 3 && order.confirmStatus >= 2 && order.isComment === false" @click="toEvaluate(order.orderId)">立即评价</view>
                            </view>
                        </view>
                    </view>
                    <u-loadmore :status="loadStatus" bgColor="#f2f2f2"></u-loadmore>
                </view>

                <view class="coreshop-emptybox" v-else>
                    <u-empty :src="$globalConstVars.apiFilesUrl+'/static/images/empty/order.png'" icon-size="300" text="您还没有相关类别的订单" mode="list"></u-empty>
                    <navigator class="coreshop-btn" url="/pages/category/index/index" open-type="switchTab">随便逛逛</navigator>
                </view>
            </view>
        </template>
    </view>
</template>

<script>
    import { orders, goods } from '@/common/mixins/mixinsHelper.js'
    import SeafoodMyReservations from '@/components/seafood/MyReservations.vue'

    export default {
        components: {
            SeafoodMyReservations
        },
        mixins: [orders, goods],
        data() {
            return {
                seafoodMode: false,
                tabs: ['全部', '待付款', '待发货', '待收货', '待评价'],
                items: ['未使用', '已使用', '已失效'],
                current: 0,
                page: 1,
                limit: 10,
                listData: [],
                loadStatus: 'loadmore',
                iconType: 'flower',
                loadText: {
                    loadmore: '轻轻上拉',
                    loading: '努力加载中',
                    nomore: '实在没有了'
                },
                status: [0, 1, 2, 3, 4],
            }
        },
        onLoad: function (option) {
            this.seafoodMode = !!(option && String(option.seafood || '') === '1');
            const pageTitle = this.seafoodMode ? '我的预订' : '订单列表';
            uni.setNavigationBarTitle({ title: pageTitle });
            // #ifdef H5
            if (typeof document !== 'undefined') {
                document.title = pageTitle;
            }
            // #endif

            if (this.seafoodMode) {
                return;
            }

            var currentIndex = option.swiperCurrentIndexId;
            if (currentIndex) {
                this.current = currentIndex;
            }
            this.getOrders();
        },
        onReachBottom() {
            if (this.seafoodMode) return;
            if (this.loadStatus === 'loadmore') {
                this.getOrders();
            }
        },
        onShow() {
            if (this.seafoodMode && this.$refs.seafoodReservations) {
                this.$refs.seafoodReservations.refreshOrders(false);
            }
        },
        methods: {
            onClickItem(index) {
                if (this.current !== index) {
                    this.current = index;
                    this.page = 1;
                    this.listData = [];
                    this.getOrders();
                }
            },
            getOrders() {
                if (this.seafoodMode) return;
                this.loadStatus = 'loading'
                let data = {
                    page: this.page,
                    limit: this.limit,
                    status: this.current
                }
                this.$u.api.orderList(data).then(res => {
                    if (res.status) {
                        res.data.list = this.formatOrderStatus(res.data.list);
                        let newList = this.listData.concat(res.data.list);
                        this.listData = newList;

                        if (res.data.count > this.listData.length) {
                            this.page++
                            this.loadStatus = 'loadmore'
                        } else {
                            this.loadStatus = 'nomore'
                        }
                    } else {
                        this.$u.toast(res.msg);
                    }
                });
            },
            removeorder: function (oid) {
                uni.showModal({
                    title: '确认提醒',
                    content: '您确定要移除订单 [ ' + oid + ' ] 吗？',
                    success: function (e) {
                        if (e.confirm) {
                        }
                    }
                });
            },
            tackDelivery(index, orderIndex) {
                let _this = this;
                this.$common.modelShow('提示', '确认执行收货操作吗?', () => {
                    let data = {
                        id: _this.listData[orderIndex].orderId
                    }
                    _this.$u.api.confirmOrder(data).then(res => {
                        if (res.status) {
                            _this.$refs.uToast.show({
                                title: '确认收货成功', type: 'success', callback: function () {
                                    if (this.tab !== 0) {
                                        _this.listData.splice(orderIndex, 1)
                                    } else {
                                        _this.getOrders();
                                    }
                                }
                            })
                        } else {
                            _this.$u.toast(res.msg)
                        }
                    })
                })
            },
            formatOrderStatus(orderList) {
                orderList.forEach(item => {
                    switch (item.status) {
                        case 1:
                            if (item.payStatus === 1) {
                                this.$set(item, 'orderStatusName', '待付款')
                            } else if (item.payStatus >= 2 && item.shipStatus === 1) {
                                this.$set(item, 'orderStatusName', '待发货')
                            } else if (item.payStatus >= 2 && item.shipStatus === 2) {
                                this.$set(item, 'orderStatusName', '部分发货')
                            } else if (item.payStatus >= 2 && item.shipStatus >= 3 && item.confirmStatus === 1) {
                                this.$set(item, 'orderStatusName', '已发货')
                            } else if (item.payStatus >= 2 && item.shipStatus >= 3 && item.confirmStatus >= 2 && item.isComment === false) {
                                this.$set(item, 'orderStatusName', '待评价')
                            } else if (item.payStatus >= 2 && item.shipStatus >= 3 && item.confirmStatus >= 2 && item.isComment === true) {
                                this.$set(item, 'orderStatusName', '已评价')
                            }
                            break
                        case 2:
                            this.$set(item, 'orderStatusName', '已完成')
                            break
                        case 3:
                            this.$set(item, 'orderStatusName', '已取消')
                            break
                    }
                });
                for (let i in orderList) {
                    for (let j in orderList[i].items) {
                        orderList[i].items[j].promotionList = JSON.parse(orderList[i].items[j].promotionList);
                    }
                }
                return orderList
            }
        },
    }
</script>

<style lang="scss">
    /* #ifndef H5 */
    page { height: 100%; background-color: #f2f2f2; }
    /* #endif */
</style>