<template>
    <view class="seafood-page">
        <u-toast ref="uToast" />
        <u-no-network></u-no-network>
        <u-navbar :is-back="false" title="今日鲜鱼" :background="navBackground" title-color="#ffffff"></u-navbar>

        <view class="hero-card">
            <view>
                <view class="hero-kicker">硇洲岛 · 今日到货</view>
                <view class="hero-title">今日鲜鱼接龙</view>
                <view class="hero-desc">库存实时更新，提交后立即为您留货</view>
            </view>
            <view class="hero-order" @click="goMyOrders">
                <u-icon name="order" size="30" color="#ffffff"></u-icon>
                <text>我的预订</text>
            </view>
        </view>

        <view class="notice-bar">
            <u-icon name="info-circle" size="28" color="#8a6337"></u-icon>
            <text>只用于选鱼、库存和预订，不在小程序内收款；货款继续按微信群原方式转账。</text>
        </view>

        <view class="section-head">
            <view>
                <view class="section-title">今日鱼货</view>
                <view class="section-subtitle">按剩余库存接龙，售完即止</view>
            </view>
            <view class="refresh-btn" @click="loadFish(true)">
                <u-icon name="reload" size="26" color="#5f6b63"></u-icon>
                <text>刷新</text>
            </view>
        </view>

        <view v-if="loading && fishList.length === 0" class="state-card">
            <u-loading mode="circle" size="46"></u-loading>
            <view class="state-text">正在读取今日库存...</view>
        </view>

        <view v-else-if="!loading && fishList.length === 0" class="state-card">
            <u-empty text="今日鱼货还未上架" mode="list"></u-empty>
        </view>

        <view v-else class="fish-list">
            <view class="fish-card" v-for="item in fishList" :key="item.id">
                <view class="fish-image-wrap" @click="goGoodsDetail(item.id)">
                    <image class="fish-image" :src="item.image" mode="aspectFill"></image>
                    <view class="soldout-mask" v-if="item.availableStock <= 0">已售罄</view>
                </view>

                <view class="fish-main">
                    <view class="fish-top" @click="goGoodsDetail(item.id)">
                        <view class="fish-name">{{ item.name }}</view>
                        <view class="fish-brief" v-if="item.brief">{{ item.brief }}</view>
                    </view>

                    <view class="price-line">
                        <view class="member-price">
                            <text class="price-label">会员价</text>
                            <text class="price-symbol">¥</text>
                            <text class="price-number">{{ formatPrice(item.price) }}</text>
                            <text class="price-unit">/{{ item.unit }}</text>
                        </view>
                        <view class="normal-price" v-if="showMarketPrice(item)">
                            非会员 ¥{{ formatPrice(item.mktprice) }}/{{ item.unit }}
                        </view>
                    </view>

                    <view class="stock-line">
                        <view class="stock-text" :class="{ danger: item.availableStock <= 3 }">
                            剩余 <text class="stock-number">{{ item.availableStock }}</text>{{ item.unit }}
                        </view>
                        <view class="stepper" :class="{ disabled: item.availableStock <= 0 }">
                            <view class="step-btn" @click.stop="decrease(item)">−</view>
                            <view class="step-value">{{ item.qty }}{{ item.unit }}</view>
                            <view class="step-btn plus" @click.stop="increase(item)">＋</view>
                        </view>
                    </view>
                </view>
            </view>
        </view>

        <view class="bottom-space"></view>

        <view class="reservation-footer">
            <view class="summary">
                <view class="summary-main">
                    已选 <text class="summary-strong">{{ selectedKinds }}</text> 种
                    <text class="summary-dot">·</text>
                    {{ selectedQuantityText }}
                </view>
                <view class="summary-price" v-if="selectedKinds > 0">预计 ¥{{ selectedAmount }}</view>
                <view class="summary-hint" v-else>请选择要预订的鱼货</view>
            </view>
            <button class="submit-btn" :class="{ disabled: selectedKinds === 0 }" :disabled="selectedKinds === 0" @click="submitReservation">
                提交预订
            </button>
        </view>

        <view class="dialog-mask" v-if="showReservationForm" @click.self="closeReservationForm">
            <view class="reservation-dialog">
                <view class="dialog-title">确认预订</view>
                <view class="dialog-subtitle">无需手机号授权，也不会唤起微信支付</view>

                <view class="selected-box">
                    <view class="selected-row" v-for="item in selectedItems" :key="item.productId">
                        <view class="selected-name">{{ item.name }}</view>
                        <view class="selected-qty">{{ item.qty }}{{ item.unit }} · ¥{{ (Number(item.price) * item.qty).toFixed(2) }}</view>
                    </view>
                    <view class="selected-total">合计：{{ selectedQuantityText }}　预计 ¥{{ selectedAmount }}</view>
                </view>

                <view class="form-label"><text class="required">*</text> 称呼 / 微信名</view>
                <input class="form-input" v-model="reservationForm.customerName" maxlength="40" placeholder="例如：杰仔、厨神五妹" />

                <view class="form-label"><text class="required">*</text> 取货方式</view>
                <view class="delivery-options">
                    <view class="delivery-option" :class="{ active: reservationForm.deliveryType === 'pickup' }" @click="reservationForm.deliveryType = 'pickup'">到店取</view>
                    <view class="delivery-option" :class="{ active: reservationForm.deliveryType === 'shipping' }" @click="reservationForm.deliveryType = 'shipping'">邮寄</view>
                </view>

                <view class="form-label">联系方式（选填）</view>
                <input class="form-input" v-model="reservationForm.contact" maxlength="80" placeholder="手机号或其他方便联系的信息" />

                <view class="form-label">备注（选填）</view>
                <textarea class="form-textarea" v-model="reservationForm.note" maxlength="500" placeholder="例如：下午到店取、邮寄地址稍后微信发" />

                <view class="dialog-actions">
                    <button class="dialog-cancel" :disabled="submitting" @click="closeReservationForm">返回修改</button>
                    <button class="dialog-confirm" :class="{ disabled: submitting }" :disabled="submitting" @click="confirmReservation">
                        {{ submitting ? '正在留货...' : '确认预订' }}
                    </button>
                </view>
            </view>
        </view>
    </view>
</template>

<script>
    import { goods } from '@/common/mixins/mixinsHelper.js';

    export default {
        mixins: [goods],
        data() {
            return {
                navBackground: { backgroundColor: '#245b43' },
                fishList: [],
                loading: false,
                submitting: false,
                loadedOnce: false,
                showReservationForm: false,
                reservationForm: {
                    customerName: '',
                    contact: '',
                    deliveryType: 'pickup',
                    note: ''
                }
            };
        },
        computed: {
            selectedItems() {
                return this.fishList.filter(item => item.qty > 0 && item.productId);
            },
            selectedKinds() {
                return this.selectedItems.length;
            },
            selectedAmount() {
                const amount = this.selectedItems.reduce((sum, item) => sum + Number(item.price || 0) * item.qty, 0);
                return amount.toFixed(2);
            },
            selectedQuantityText() {
                if (this.selectedItems.length === 0) return '0斤';
                const units = [...new Set(this.selectedItems.map(item => item.unit || '斤'))];
                const total = this.selectedItems.reduce((sum, item) => sum + item.qty, 0);
                return units.length === 1 ? `${total}${units[0]}` : `${total}份`;
            }
        },
        onLoad() {
            this.loadFish();
        },
        onShow() {
            if (this.loadedOnce && !this.loading && !this.showReservationForm) {
                this.loadFish(false, true);
            }
        },
        async onPullDownRefresh() {
            try {
                await this.loadFish(true);
            } finally {
                uni.stopPullDownRefresh();
            }
        },
        methods: {
            async loadFish(showToast = false, preserveSelection = false) {
                if (this.loading) return;
                this.loading = true;

                const oldQty = {};
                if (preserveSelection) {
                    this.fishList.forEach(item => {
                        oldQty[item.id] = item.qty || 0;
                    });
                }

                try {
                    const listRes = await this.$u.api.goodsList({
                        where: JSON.stringify({}),
                        limit: 50,
                        page: 1,
                        order: 'sort asc'
                    });

                    if (!listRes.status || !listRes.data || !listRes.data.list) {
                        throw new Error(listRes.msg || '鱼货读取失败');
                    }

                    const baseList = Array.from(listRes.data.list);
                    const detailTasks = baseList.map(item => this.$u.api.goodsDetail({ id: item.id })
                        .then(res => ({ item, res }))
                        .catch(() => ({ item, res: null })));
                    const details = await Promise.all(detailTasks);

                    this.fishList = details.map(({ item, res }) => {
                        const detail = res && res.status && res.data ? res.data : item;
                        const product = detail.product || {};
                        const availableStock = Math.max(0, Number(product.stock !== undefined ? product.stock : detail.stock || 0));
                        const previousQty = preserveSelection ? Number(oldQty[item.id] || 0) : 0;

                        return {
                            id: detail.id || item.id,
                            name: detail.name || item.name || '今日鱼货',
                            brief: detail.brief || item.brief || '',
                            image: detail.image || item.image || '/static/images/common/empty-banner.png',
                            unit: detail.unit || '斤',
                            price: Number(product.price !== undefined ? product.price : detail.price || 0),
                            mktprice: Number(product.mktprice !== undefined ? product.mktprice : detail.mktprice || 0),
                            productId: Number(product.id || 0),
                            availableStock,
                            qty: Math.min(previousQty, availableStock)
                        };
                    }).filter(item => item.productId > 0);

                    this.loadedOnce = true;
                    if (showToast) this.$u.toast('库存已更新');
                } catch (error) {
                    this.$u.toast(error && error.message ? error.message : '鱼货读取失败，请稍后重试');
                } finally {
                    this.loading = false;
                }
            },
            increase(item) {
                if (!item.productId || item.availableStock <= 0) return;
                if (item.qty >= item.availableStock) {
                    this.$u.toast(`当前最多可订${item.availableStock}${item.unit}`);
                    return;
                }
                item.qty += 1;
            },
            decrease(item) {
                if (item.qty > 0) item.qty -= 1;
            },
            formatPrice(value) {
                const num = Number(value || 0);
                return num.toFixed(2).replace(/\.?0+$/, '');
            },
            showMarketPrice(item) {
                return Number(item.mktprice || 0) > 0 && Number(item.mktprice) !== Number(item.price);
            },
            submitReservation() {
                if (this.selectedItems.length === 0) return;
                this.showReservationForm = true;
            },
            closeReservationForm() {
                if (this.submitting) return;
                this.showReservationForm = false;
            },
            async confirmReservation() {
                if (this.submitting || this.selectedItems.length === 0) return;

                const customerName = (this.reservationForm.customerName || '').trim();
                if (!customerName) {
                    this.$u.toast('请填写称呼或微信名');
                    return;
                }

                this.submitting = true;
                const requestId = `${Date.now()}-${Math.random().toString(16).slice(2)}`;

                try {
                    const res = await this.$u.post('/Api/SeafoodReservation/Create', {
                        customerName,
                        contact: (this.reservationForm.contact || '').trim(),
                        deliveryType: this.reservationForm.deliveryType,
                        note: (this.reservationForm.note || '').trim(),
                        requestId,
                        items: this.selectedItems.map(item => ({
                            productId: item.productId,
                            quantity: item.qty
                        }))
                    }, {
                        method: 'seafoodReservation.create',
                        needToken: false
                    });

                    if (!res || !res.status) {
                        throw new Error((res && res.msg) || '预订失败，请重新确认库存');
                    }

                    const history = uni.getStorageSync('seafoodReservationHistory') || [];
                    history.unshift({
                        reservationNo: res.data.reservationNo,
                        lookupToken: res.data.lookupToken,
                        customerName: res.data.customerName,
                        deliveryType: res.data.deliveryType,
                        totalAmount: res.data.totalAmount,
                        createdAt: Date.now()
                    });
                    uni.setStorageSync('seafoodReservationHistory', history.slice(0, 30));

                    this.showReservationForm = false;
                    this.fishList.forEach(item => { item.qty = 0; });
                    this.reservationForm.note = '';
                    await this.loadFish(false, false);

                    uni.showModal({
                        title: '预订成功',
                        content: `预订号：${res.data.reservationNo}\n预计金额：¥${Number(res.data.totalAmount || 0).toFixed(2)}\n已为您留货，请按微信群原方式转账。`,
                        showCancel: false,
                        confirmText: '知道了'
                    });
                } catch (error) {
                    this.$u.toast(error && error.message ? error.message : '提交失败，请稍后重试');
                    await this.loadFish(false, true);
                } finally {
                    this.submitting = false;
                }
            },
            goMyOrders() {
                const history = uni.getStorageSync('seafoodReservationHistory') || [];
                if (!history.length) {
                    this.$u.toast('本机还没有预订记录');
                    return;
                }

                uni.navigateTo({
                    url: '/pages/member/order/index/index?seafood=1'
                });
            }
        }
    };
</script>

<style lang="scss">
    .seafood-page {
        min-height: 100vh;
        background: #f4f6f3;
        padding-bottom: 260rpx;
    }

    .hero-card {
        margin: 24rpx;
        padding: 34rpx 30rpx;
        border-radius: 24rpx;
        background: linear-gradient(135deg, #245b43 0%, #39775b 100%);
        color: #ffffff;
        display: flex;
        justify-content: space-between;
        align-items: center;
        box-shadow: 0 10rpx 28rpx rgba(36, 91, 67, 0.18);
    }

    .hero-kicker { font-size: 22rpx; opacity: 0.82; letter-spacing: 2rpx; }
    .hero-title { margin-top: 8rpx; font-size: 38rpx; font-weight: 700; }
    .hero-desc { margin-top: 12rpx; font-size: 24rpx; opacity: 0.86; }

    .hero-order {
        flex-shrink: 0;
        margin-left: 18rpx;
        padding: 16rpx 18rpx;
        border-radius: 16rpx;
        background: rgba(255, 255, 255, 0.14);
        display: flex;
        flex-direction: column;
        align-items: center;
        font-size: 22rpx;
    }
    .hero-order text { margin-top: 6rpx; }

    .notice-bar {
        margin: 0 24rpx 24rpx;
        padding: 20rpx 22rpx;
        border-radius: 16rpx;
        background: #fff5df;
        color: #785a35;
        display: flex;
        align-items: flex-start;
        font-size: 24rpx;
        line-height: 1.6;
    }
    .notice-bar text { flex: 1; margin-left: 12rpx; }

    .section-head { padding: 6rpx 28rpx 18rpx; display: flex; justify-content: space-between; align-items: flex-end; }
    .section-title { font-size: 34rpx; font-weight: 700; color: #26342d; }
    .section-subtitle { margin-top: 6rpx; font-size: 22rpx; color: #8a948e; }
    .refresh-btn { display: flex; align-items: center; font-size: 24rpx; color: #5f6b63; }
    .refresh-btn text { margin-left: 6rpx; }

    .state-card {
        margin: 0 24rpx;
        min-height: 300rpx;
        border-radius: 20rpx;
        background: #ffffff;
        display: flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
    }
    .state-text { margin-top: 18rpx; font-size: 25rpx; color: #8a948e; }

    .fish-list { padding: 0 24rpx; }
    .fish-card { margin-bottom: 18rpx; padding: 18rpx; border-radius: 22rpx; background: #ffffff; display: flex; box-shadow: 0 4rpx 18rpx rgba(35, 56, 45, 0.05); }
    .fish-image-wrap { position: relative; width: 190rpx; height: 190rpx; flex-shrink: 0; overflow: hidden; border-radius: 18rpx; background: #edf0ed; }
    .fish-image { width: 100%; height: 100%; }
    .soldout-mask { position: absolute; left: 0; right: 0; bottom: 0; padding: 8rpx 0; text-align: center; background: rgba(31, 39, 35, 0.72); color: #ffffff; font-size: 22rpx; }
    .fish-main { min-width: 0; flex: 1; margin-left: 20rpx; display: flex; flex-direction: column; justify-content: space-between; }
    .fish-name { font-size: 31rpx; font-weight: 700; color: #26342d; line-height: 1.35; }
    .fish-brief { margin-top: 7rpx; font-size: 22rpx; color: #919b95; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .price-line { margin-top: 10rpx; }
    .member-price { display: flex; align-items: baseline; color: #c84b3b; }
    .price-label { margin-right: 8rpx; padding: 3rpx 8rpx; border-radius: 7rpx; background: #fbe8e4; font-size: 20rpx; color: #ba4b3c; }
    .price-symbol { font-size: 24rpx; font-weight: 700; }
    .price-number { font-size: 37rpx; font-weight: 800; }
    .price-unit { margin-left: 3rpx; font-size: 22rpx; }
    .normal-price { margin-top: 3rpx; font-size: 21rpx; color: #a2aaa5; }
    .stock-line { margin-top: 10rpx; display: flex; justify-content: space-between; align-items: center; }
    .stock-text { font-size: 23rpx; color: #68736c; }
    .stock-text.danger { color: #c34d42; }
    .stock-number { margin: 0 3rpx; font-size: 27rpx; font-weight: 700; }

    .stepper { height: 58rpx; display: flex; align-items: center; overflow: hidden; border: 1rpx solid #dfe5e1; border-radius: 14rpx; background: #f8faf8; }
    .stepper.disabled { opacity: 0.45; }
    .step-btn { width: 56rpx; height: 58rpx; line-height: 56rpx; text-align: center; font-size: 32rpx; color: #4f5d55; }
    .step-btn.plus { color: #245b43; font-weight: 700; }
    .step-value { min-width: 82rpx; padding: 0 8rpx; text-align: center; font-size: 24rpx; color: #26342d; border-left: 1rpx solid #e2e7e4; border-right: 1rpx solid #e2e7e4; }
    .bottom-space { height: 26rpx; }

    .reservation-footer {
        position: fixed;
        left: 0;
        right: 0;
        bottom: 100rpx;
        z-index: 20;
        min-height: 118rpx;
        padding: 16rpx 24rpx calc(16rpx + env(safe-area-inset-bottom));
        background: rgba(255, 255, 255, 0.98);
        border-top: 1rpx solid #e8ece9;
        display: flex;
        align-items: center;
    }
    .summary { min-width: 0; flex: 1; margin-right: 18rpx; }
    .summary-main { font-size: 25rpx; color: #4f5d55; }
    .summary-strong { font-weight: 700; color: #245b43; }
    .summary-dot { margin: 0 8rpx; color: #adb5b0; }
    .summary-price { margin-top: 5rpx; font-size: 23rpx; font-weight: 600; color: #c84b3b; }
    .summary-hint { margin-top: 5rpx; font-size: 22rpx; color: #9aa39e; }

    .submit-btn {
        width: 230rpx;
        height: 78rpx;
        line-height: 78rpx;
        margin: 0;
        padding: 0;
        border: 0;
        border-radius: 39rpx;
        background: #245b43;
        color: #ffffff;
        font-size: 29rpx;
        font-weight: 700;
    }
    .submit-btn::after { border: 0; }
    .submit-btn.disabled { background: #b9c3bd; }

    .dialog-mask {
        position: fixed;
        z-index: 1000;
        left: 0;
        right: 0;
        top: 0;
        bottom: 0;
        padding: 40rpx 24rpx;
        background: rgba(20, 29, 24, 0.56);
        display: flex;
        align-items: center;
        justify-content: center;
    }

    .reservation-dialog {
        width: 100%;
        max-width: 680rpx;
        max-height: 88vh;
        overflow-y: auto;
        padding: 34rpx 30rpx 30rpx;
        border-radius: 26rpx;
        background: #ffffff;
        box-shadow: 0 24rpx 70rpx rgba(0, 0, 0, 0.2);
    }
    .dialog-title { font-size: 36rpx; font-weight: 800; color: #26342d; }
    .dialog-subtitle { margin-top: 8rpx; font-size: 22rpx; color: #89948e; }

    .selected-box { margin-top: 24rpx; padding: 18rpx 20rpx; border-radius: 16rpx; background: #f4f7f5; }
    .selected-row { display: flex; justify-content: space-between; align-items: center; padding: 7rpx 0; }
    .selected-name { max-width: 52%; font-size: 24rpx; font-weight: 600; color: #35433b; }
    .selected-qty { font-size: 23rpx; color: #68736c; }
    .selected-total { margin-top: 10rpx; padding-top: 14rpx; border-top: 1rpx solid #dfe6e2; font-size: 25rpx; font-weight: 700; color: #c84b3b; }

    .form-label { margin-top: 22rpx; margin-bottom: 10rpx; font-size: 24rpx; font-weight: 600; color: #3f4d45; }
    .required { margin-right: 4rpx; color: #c84b3b; }
    .form-input, .form-textarea { box-sizing: border-box; width: 100%; border: 1rpx solid #dfe5e1; border-radius: 14rpx; background: #fafbfa; font-size: 25rpx; color: #26342d; }
    .form-input { height: 76rpx; padding: 0 20rpx; }
    .form-textarea { min-height: 128rpx; padding: 18rpx 20rpx; line-height: 1.5; }

    .delivery-options { display: flex; gap: 16rpx; }
    .delivery-option { flex: 1; height: 72rpx; line-height: 72rpx; text-align: center; border: 1rpx solid #d9e1dc; border-radius: 14rpx; background: #f8faf8; font-size: 25rpx; color: #56635c; }
    .delivery-option.active { border-color: #245b43; background: #eaf3ee; color: #245b43; font-weight: 700; }

    .dialog-actions { margin-top: 30rpx; display: flex; gap: 18rpx; }
    .dialog-cancel, .dialog-confirm { flex: 1; height: 78rpx; line-height: 78rpx; margin: 0; padding: 0; border: 0; border-radius: 39rpx; font-size: 27rpx; font-weight: 700; }
    .dialog-cancel { background: #edf1ee; color: #56635c; }
    .dialog-confirm { background: #245b43; color: #ffffff; }
    .dialog-confirm.disabled { background: #9eb0a6; }
    .dialog-cancel::after, .dialog-confirm::after { border: 0; }
</style>
