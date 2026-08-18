<template>
    <view class="reservation-page">
        <u-toast ref="uToast" />
        <u-no-network></u-no-network>
        <u-navbar title="预订成功"></u-navbar>

        <view v-if="type === 1" class="reservation-wrap">
            <view class="success-card">
                <view class="success-icon">
                    <u-icon name="checkmark-circle" size="92" color="#22a06b"></u-icon>
                </view>
                <view class="success-title">预订已提交</view>
                <view class="success-desc">鱼货已按订单占用库存，请按微信群原来的方式转账。</view>
            </view>

            <view class="info-card">
                <view class="info-row">
                    <text class="label">订单编号</text>
                    <text class="value">{{ displayOrderId }}</text>
                </view>
                <view class="info-row" v-if="orderInfo.money !== undefined && orderInfo.money !== null && orderInfo.money !== ''">
                    <text class="label">预计金额</text>
                    <text class="value price">¥{{ orderInfo.money }}</text>
                </view>
                <view class="info-row">
                    <text class="label">付款方式</text>
                    <text class="value">微信转账 / 线下确认</text>
                </view>
                <view class="info-row no-border">
                    <text class="label">订单状态</text>
                    <text class="value status">已预订</text>
                </view>
            </view>

            <view class="notice-card">
                <view class="notice-title">温馨提示</view>
                <view class="notice-text">1. 本小程序暂不收款，不会唤起微信支付。</view>
                <view class="notice-text">2. 下单后库存已经为您保留，请按群内原有方式转账。</view>
                <view class="notice-text">3. 海鲜实际重量如有少量出入，以最终称重和负责人确认为准。</view>
            </view>

            <view class="action-box" v-if="sourceOrderId">
                <u-button type="success" @click="viewOrder">查看订单详情</u-button>
            </view>
        </view>

        <view v-else class="disabled-card">
            <u-icon name="info-circle" size="70" color="#909399"></u-icon>
            <view class="disabled-title">在线支付已关闭</view>
            <view class="disabled-text">当前版本仅用于鱼货预订、库存和订单记录，不提供充值或其他在线支付功能。</view>
        </view>

        <coreshop-login-modal></coreshop-login-modal>
    </view>
</template>

<script>
    import { orders } from '@/common/mixins/mixinsHelper.js';

    export default {
        mixins: [orders],
        data() {
            return {
                orderId: '',
                type: 1,
                orderInfo: {}
            };
        },
        computed: {
            sourceOrderId() {
                if (this.orderInfo && this.orderInfo.rel && this.orderInfo.rel.length > 0) {
                    return this.orderInfo.rel[0].sourceId || '';
                }
                return this.orderId || '';
            },
            displayOrderId() {
                return this.sourceOrderId || '已生成';
            }
        },
        onLoad(options) {
            this.orderId = options.orderId || '';
            this.type = Number(options.type || 1);

            if (this.type === 1 && this.orderId) {
                this.getOrderInfo();
            }
        },
        methods: {
            getOrderInfo() {
                const data = {
                    ids: this.orderId,
                    paymentType: 1
                };

                this.$u.api.paymentsCheckpay(data).then(res => {
                    if (res.status) {
                        this.orderInfo = res.data || {};
                    }
                }).catch(() => {
                    // 预订订单已经创建成功；金额信息读取失败时仍保留成功页，避免误导用户重复下单。
                });
            },
            viewOrder() {
                this.goOrderDetail(this.sourceOrderId);
            }
        }
    };
</script>

<style lang="scss">
    @import "pay.scss";
</style>
