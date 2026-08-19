<template>
    <view class="seafood-orders-page">
        <view class="seafood-orders-hero">
            <view>
                <view class="hero-title">我的鲜鱼预订</view>
                <view class="hero-subtitle">状态以后台最新处理结果为准</view>
            </view>
            <view class="refresh-button" :class="{ disabled: loading }" @click="refreshOrders(true)">
                <u-icon name="reload" size="28" color="#245b43"></u-icon>
                <text>{{ loading ? '刷新中' : '刷新' }}</text>
            </view>
        </view>

        <view class="status-summary" v-if="orders.length">
            <view class="summary-cell">
                <text class="summary-number">{{ orders.length }}</text>
                <text class="summary-label">全部</text>
            </view>
            <view class="summary-cell pending">
                <text class="summary-number">{{ statusCount(0) }}</text>
                <text class="summary-label">待处理</text>
            </view>
            <view class="summary-cell confirmed">
                <text class="summary-number">{{ statusCount(1) }}</text>
                <text class="summary-label">已确认</text>
            </view>
            <view class="summary-cell completed">
                <text class="summary-number">{{ statusCount(2) }}</text>
                <text class="summary-label">已完成</text>
            </view>
            <view class="summary-cell cancelled">
                <text class="summary-number">{{ statusCount(3) }}</text>
                <text class="summary-label">已取消</text>
            </view>
        </view>

        <view class="notice-card">
            <u-icon name="info-circle" size="28" color="#8a6337"></u-icon>
            <text>这里显示本机提交过的鲜鱼预订；后台确认、完成或取消后，点刷新即可看到最新状态。</text>
        </view>

        <view v-if="loading && orders.length === 0" class="state-card">
            <u-loading mode="circle" size="46"></u-loading>
            <view class="state-text">正在读取最新预订状态...</view>
        </view>

        <view v-else-if="!loading && orders.length === 0" class="state-card empty">
            <u-empty text="本机还没有鲜鱼预订" mode="order"></u-empty>
            <view class="state-subtext">返回首页选择鱼货后即可提交预订</view>
        </view>

        <view v-else class="order-list">
            <view class="order-card" v-for="order in orders" :key="order.lookupToken || order.reservationNo">
                <view class="order-head">
                    <view class="order-head-main">
                        <view class="order-no">{{ order.reservationNo || '预订记录' }}</view>
                        <view class="order-time">{{ formatTime(order.createdAt) }}</view>
                    </view>
                    <view class="status-badge" :class="statusClass(order.status, order.stale)">
                        {{ statusText(order.status, order.stale) }}
                    </view>
                </view>

                <view class="customer-line">
                    <text class="customer-name">{{ order.customerName || '未填写称呼' }}</text>
                    <text class="delivery-badge">{{ deliveryText(order.deliveryType) }}</text>
                </view>

                <view class="item-list" v-if="order.items && order.items.length">
                    <view class="item-row" v-for="item in order.items" :key="item.id || (item.productId + '-' + item.goodsName)">
                        <view class="item-main">
                            <view class="item-name">{{ item.goodsName || '鱼货' }}</view>
                            <view class="item-price">单价 ¥{{ formatMoney(item.unitPrice) }}/{{ item.unit || '斤' }}</view>
                        </view>
                        <view class="item-right">
                            <view class="item-qty">{{ item.quantity }}{{ item.unit || '斤' }}</view>
                            <view class="item-amount">¥{{ formatMoney(item.amount) }}</view>
                        </view>
                    </view>
                </view>

                <view class="stale-message" v-else-if="order.stale">
                    暂时无法读取这笔预订的最新详情，请稍后刷新。
                </view>

                <view class="order-footer">
                    <view class="contact-note">
                        <text v-if="order.contact">联系：{{ order.contact }}</text>
                        <text v-else>货款继续按微信群原方式转账</text>
                    </view>
                    <view class="order-total">
                        合计 <text>¥{{ formatMoney(order.totalAmount) }}</text>
                    </view>
                </view>

                <view class="remark" v-if="order.note">备注：{{ order.note }}</view>
            </view>
        </view>

        <view class="bottom-space"></view>
    </view>
</template>

<script>
    export default {
        data() {
            return {
                loading: false,
                orders: []
            };
        },
        created() {
            this.refreshOrders(false);
        },
        methods: {
            async refreshOrders(showToast = false) {
                if (this.loading) return;

                const rawHistory = uni.getStorageSync('seafoodReservationHistory') || [];
                const history = Array.isArray(rawHistory) ? rawHistory : [];
                const seen = {};
                const localOrders = history.filter(item => {
                    const token = item && item.lookupToken ? String(item.lookupToken) : '';
                    if (!token || seen[token]) return false;
                    seen[token] = true;
                    return true;
                }).slice(0, 30);

                if (!localOrders.length) {
                    this.orders = [];
                    if (showToast) this.$u.toast('本机还没有预订记录');
                    return;
                }

                this.loading = true;
                try {
                    const results = await Promise.all(localOrders.map(async local => {
                        try {
                            const res = await this.$u.post('/Api/SeafoodReservation/Get', {
                                lookupToken: local.lookupToken
                            }, {
                                method: 'seafoodReservation.get',
                                needToken: false
                            });

                            if (!res || !res.status || !res.data) {
                                throw new Error((res && res.msg) || '预订状态读取失败');
                            }

                            return {
                                lookupToken: local.lookupToken,
                                reservationNo: res.data.reservationNo || local.reservationNo,
                                customerName: res.data.customerName || local.customerName,
                                contact: res.data.contact || '',
                                deliveryType: res.data.deliveryType || local.deliveryType,
                                note: res.data.note || '',
                                totalAmount: Number(res.data.totalAmount !== undefined ? res.data.totalAmount : local.totalAmount || 0),
                                status: Number(res.data.status),
                                createdAt: res.data.createdAt || local.createdAt,
                                items: Array.isArray(res.data.items) ? res.data.items : [],
                                stale: false
                            };
                        } catch (error) {
                            return {
                                lookupToken: local.lookupToken,
                                reservationNo: local.reservationNo,
                                customerName: local.customerName,
                                contact: '',
                                deliveryType: local.deliveryType,
                                note: '',
                                totalAmount: Number(local.totalAmount || 0),
                                status: null,
                                createdAt: local.createdAt,
                                items: [],
                                stale: true
                            };
                        }
                    }));

                    this.orders = results;
                    if (showToast) this.$u.toast('预订状态已更新');
                } finally {
                    this.loading = false;
                }
            },
            statusCount(status) {
                return this.orders.filter(item => !item.stale && Number(item.status) === status).length;
            },
            statusText(status, stale) {
                if (stale) return '待刷新';
                if (Number(status) === 0) return '待处理';
                if (Number(status) === 1) return '已确认';
                if (Number(status) === 2) return '已完成';
                if (Number(status) === 3) return '已取消';
                return '未知状态';
            },
            statusClass(status, stale) {
                if (stale) return 'stale';
                if (Number(status) === 0) return 'pending';
                if (Number(status) === 1) return 'confirmed';
                if (Number(status) === 2) return 'completed';
                if (Number(status) === 3) return 'cancelled';
                return 'stale';
            },
            deliveryText(deliveryType) {
                return deliveryType === 'shipping' ? '邮寄' : '到店取';
            },
            formatMoney(value) {
                return Number(value || 0).toFixed(2);
            },
            formatTime(value) {
                if (!value) return '-';
                if (typeof value === 'number') {
                    return this.$u.timeFormat(value, 'yyyy-mm-dd hh:MM');
                }
                const text = String(value).replace('T', ' ').replace(/\.\d+.*$/, '');
                return text.length > 16 ? text.slice(0, 16) : text;
            }
        }
    };
</script>

<style lang="scss" scoped>
    .seafood-orders-page {
        min-height: 100vh;
        box-sizing: border-box;
        padding: 24rpx;
        background: #f4f6f3;
    }

    .seafood-orders-hero {
        padding: 26rpx 28rpx;
        border-radius: 22rpx;
        background: #ffffff;
        display: flex;
        justify-content: space-between;
        align-items: center;
        box-shadow: 0 4rpx 18rpx rgba(35, 56, 45, 0.05);
    }
    .hero-title { font-size: 34rpx; font-weight: 800; color: #26342d; }
    .hero-subtitle { margin-top: 6rpx; font-size: 22rpx; color: #8a948e; }
    .refresh-button { min-width: 120rpx; height: 64rpx; padding: 0 18rpx; border-radius: 32rpx; background: #eaf3ee; color: #245b43; display: flex; align-items: center; justify-content: center; font-size: 24rpx; }
    .refresh-button text { margin-left: 7rpx; }
    .refresh-button.disabled { opacity: 0.6; }

    .status-summary {
        margin-top: 18rpx;
        padding: 18rpx 12rpx;
        border-radius: 20rpx;
        background: #ffffff;
        display: flex;
        box-shadow: 0 4rpx 18rpx rgba(35, 56, 45, 0.04);
    }
    .summary-cell { flex: 1; min-width: 0; text-align: center; color: #245b43; }
    .summary-cell.pending { color: #c9801b; }
    .summary-cell.confirmed { color: #3d76b5; }
    .summary-cell.completed { color: #2f7a58; }
    .summary-cell.cancelled { color: #929a95; }
    .summary-number { display: block; font-size: 31rpx; font-weight: 800; }
    .summary-label { display: block; margin-top: 4rpx; font-size: 20rpx; color: #7f8983; white-space: nowrap; }

    .notice-card {
        margin-top: 18rpx;
        padding: 19rpx 22rpx;
        border-radius: 16rpx;
        background: #fff5df;
        color: #785a35;
        display: flex;
        align-items: flex-start;
        font-size: 23rpx;
        line-height: 1.55;
    }
    .notice-card text { flex: 1; margin-left: 10rpx; }

    .state-card {
        margin-top: 18rpx;
        min-height: 360rpx;
        border-radius: 20rpx;
        background: #ffffff;
        display: flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
    }
    .state-text { margin-top: 18rpx; font-size: 24rpx; color: #8a948e; }
    .state-subtext { margin-top: 16rpx; font-size: 22rpx; color: #9ba49f; }

    .order-list { margin-top: 18rpx; }
    .order-card {
        margin-bottom: 18rpx;
        padding: 24rpx;
        border-radius: 22rpx;
        background: #ffffff;
        box-shadow: 0 4rpx 18rpx rgba(35, 56, 45, 0.05);
    }
    .order-head { display: flex; justify-content: space-between; align-items: flex-start; }
    .order-head-main { min-width: 0; flex: 1; margin-right: 16rpx; }
    .order-no { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 23rpx; font-weight: 700; color: #3c4942; }
    .order-time { margin-top: 7rpx; font-size: 21rpx; color: #9ba39e; }

    .status-badge { flex-shrink: 0; padding: 8rpx 16rpx; border-radius: 18rpx; font-size: 22rpx; font-weight: 700; }
    .status-badge.pending { background: #fff1da; color: #b97515; }
    .status-badge.confirmed { background: #e8f1fb; color: #3772ae; }
    .status-badge.completed { background: #e5f4ec; color: #287653; }
    .status-badge.cancelled { background: #f0f1f0; color: #858e89; }
    .status-badge.stale { background: #f7efe3; color: #8b6e43; }

    .customer-line { margin-top: 20rpx; display: flex; align-items: center; }
    .customer-name { font-size: 29rpx; font-weight: 800; color: #26342d; }
    .delivery-badge { margin-left: 12rpx; padding: 4rpx 10rpx; border-radius: 8rpx; background: #eef4f0; color: #557064; font-size: 20rpx; }

    .item-list { margin-top: 18rpx; border-top: 1rpx solid #edf0ee; }
    .item-row { padding: 18rpx 0; border-bottom: 1rpx solid #edf0ee; display: flex; justify-content: space-between; align-items: center; }
    .item-main { min-width: 0; flex: 1; margin-right: 18rpx; }
    .item-name { font-size: 27rpx; font-weight: 700; color: #34423a; }
    .item-price { margin-top: 7rpx; font-size: 21rpx; color: #929b96; }
    .item-right { flex-shrink: 0; text-align: right; }
    .item-qty { font-size: 25rpx; font-weight: 700; color: #45534b; }
    .item-amount { margin-top: 6rpx; font-size: 22rpx; color: #c84b3b; }

    .stale-message { margin-top: 18rpx; padding: 18rpx; border-radius: 14rpx; background: #faf6ef; color: #8a704b; font-size: 22rpx; line-height: 1.5; }

    .order-footer { padding-top: 18rpx; display: flex; justify-content: space-between; align-items: flex-end; }
    .contact-note { min-width: 0; flex: 1; margin-right: 16rpx; font-size: 21rpx; color: #8b958f; line-height: 1.45; }
    .order-total { flex-shrink: 0; font-size: 23rpx; color: #657169; }
    .order-total text { margin-left: 6rpx; font-size: 31rpx; font-weight: 800; color: #c84b3b; }
    .remark { margin-top: 14rpx; padding-top: 14rpx; border-top: 1rpx dashed #e4e8e5; font-size: 22rpx; color: #7d8882; line-height: 1.5; }
    .bottom-space { height: 36rpx; }
</style>
