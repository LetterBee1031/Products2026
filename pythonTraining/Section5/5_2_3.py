import numpy as np
import matplotlib.pyplot as plt
from mpl_toolkits.mplot3d import Axes3D

np.random.seed(seed=1)              # 乱数の設定
X_min = 4                           # Xの下限（表示用）
X_max = 30                          # Xの上限（表示用）
X_n = 16                            # データの個数

X = 5 + 25 * np.random.rand(X_n)    # Xの生成（年齢データ）
Prm_c = [170, 108, 0.2]             # 生成パラメータ
T = Prm_c[0] - Prm_c[1] * np.exp(-Prm_c[2] * X) + 4 * np.random.randn(X_n)  # Xを用いてTを生成（身長データ）

np.savez('ch5_data.npz', X=X, X_min=X_min, X_max=X_max, X_n=X_n, T=T)       # データの保存

# 平均誤差関数
def mse_line(x, t, w):
    y = w[0] * x + w[1]
    mse = np.mean((y - t)**2)
    return mse

# 平均二乗誤差の勾配
def dmse_line(x, t, w):
    y = w[0] * x + w[1]
    d_w0 = 2 * np.mean((y - t) * x)
    d_w1 = 2 * np.mean(y - t)
    return d_w0, d_w1

# 勾配降下法の実装
def fit_line_num(x, t):
    w_init = [10, 165]  # 初期パラメータ
    alpha = 0.0001      # 学習率α
    tau_max = 100000    # 繰り返しの最大数
    eps = 0.1           # 繰り返しをやめる勾配の絶対値の閾値
    w_hist = np.zeros([tau_max, 2]) # 勾配降下中のwの履歴っぽい
    w_hist[0, :] = w_init           # 初期値を代入

    # 勾配降下のループ　1から初めて0回目をスキップ？
    for tau in range(1, tau_max):   
        dmse = dmse_line(x, t, w_hist[tau - 1]) # 勾配の計算
        w_hist[tau, 0] = w_hist[tau - 1, 0] - alpha * dmse[0] # w_0方向の更新計算
        w_hist[tau, 1] = w_hist[tau - 1, 1] - alpha * dmse[1] # w_1方向の更新計算

        if max(np.absolute(dmse)) < eps: # 勾配降下の終了判定
            break
    
    w0 = w_hist[tau, 0]         # w0の最終値
    w1 = w_hist[tau, 1]         # w1の最終値
    w_hist = w_hist[:tau, :]    # よくわからんが，最終結果を除いた履歴を抽出してるっぽい？別に最終結果入れたままでもいいのでは？
    return w0, w1, dmse, w_hist

# 線の表示
def show_line(w):
    xb = np.linspace(X_min, X_max, 100)
    y = w[0] * xb + w[1]
    plt.plot(xb, y, color=(.5, .5, .5), linewidth=4)

# 解析解の計算
def fit_line(x, t):
    mx = np.mean(x)
    mt = np.mean(t)
    mtx = np.mean(t * x)
    mxx = np.mean(x * x)
    w0 = (mtx - mt * mx) / (mxx - mx**2)
    w1 = mt - w0 * mx
    return np.array([w0, w1])




# d_w = dmse_line(X, T, [10, 165])
# print(np.round(d_w, 1))

# # メイン関数的な
# # MSEの等高線表示
# plt.figure(figsize=(4, 4))
# wn = 100
# w0_range = [-25, 25]
# w1_range = [120, 170]

# w0 = np.linspace(w0_range[0], w0_range[1], wn)
# w1 = np.linspace(w1_range[0], w1_range[1], wn)
# ww0, ww1 = np.meshgrid(w0, w1)
# J = np.zeros((len(w0), len(w1)))

# for i0 in range(wn):
#     for i1 in range(wn):
#         J[i1, i0] = mse_line(X, T, (w0[i0], w1[i1]))

# cont = plt.contour(ww0, ww1, J, 30, colors='black', levels=(100, 1000, 10000, 100000))
# cont.clabel(fmt='%d', fontsize=8)
# plt.grid(True)

# # 勾配法呼び出し
# W0, W1, dMSE, w_history = fit_line_num(X, T)

# # 結果表示
# print(' 繰り返し回数 {0}'.format(w_history.shape[0]))            # 勾配降下の繰り返し回数
# print('W=[{0:.6f}, {1:.6f}]'.format(W0, W1))                    # w_0, w_1の値
# print('dMSE=[{0:.6f}, {1:.6f}]'.format(dMSE[0], dMSE[1]))       # dMSE 平均二乗誤差の勾配
# print('MSE=[{0:.6f}]'.format(mse_line(X, T, [W0, W1])))         # MSE  平均二乗誤差
# plt.plot(w_history[:, 0], w_history[:, 1], '.-', color='gray', markersize=10, markeredgecolor='cornflowerblue')

# plt.figure(figsize=(4, 4))
# W = np.array([W0, W1])
# mse = mse_line(X, T, W)
# print('W0={0:.3f}, W1={1:.3f}'.format(W0, W1))
# print("SD={0:.3f} cm".format(np.sqrt(mse)))
# show_line(W)
# plt.plot(X, T, marker='o', linestyle='None', markeredgecolor='black', color='cornflowerblue')
# plt.xlim(X_min, X_max)
# plt.grid(True)
# plt.show()

# メイン関数的な
# W = fit_line(X, T)
# print('W0={0:.3f}, W1={1:.3f}'.format(W[0], W[1]))

# mse = mse_line(X, T, W)
# print("SD={0:.3f} cm".format(np.sqrt(mse)))

# plt.figure(figsize=(4, 4))
# show_line(W)
# plt.plot(X, T, marker='o', linestyle='None', markeredgecolor='black', color='cornflowerblue')
# plt.xlim(X_min, X_max)
# plt.grid(True)
# plt.show()

# 2次元データ生成
X0 = X
X0_min = 5
X0_max = 30

np.random.seed(seed=1) # 乱数固定
X1 = 23 * (T / 100)**2 + 2 * np.random.randn(X_n)
X1_min = 40
X1_max = 75

# 2次元データの表示
def show_data2(ax, x0, x1, t):
    for i in range(len(x0)):
        ax.plot([x0[i], x0[i]], [x1[i], x1[i]],[120, t[i]], color='gray')
    ax.plot(x0, x1, t, 'o', markeredgecolor='black', color='cornflowerblue', markersize=6, markeredgewidth=0.5)
    ax.view_init(elev=35, azim=-75)

# 面の表示
def show_plane(ax, w):
    px0 = np.linspace(X0_min, X0_max, 5)
    px1 = np.linspace(X1_min, X1_max, 5)
    px0, px1 = np.meshgrid(px0, px1)
    y = w[0] * px0 + w[1] * px1 + w[2]
    ax.plot_surface(px0, px1, y, rstride=1, cstride=1, alpha=0.3, color='blue', edgecolor='black')


# 面のMSE
def mse_plane(x0, x1, t, w):
    y = w[0] * x0 + w[1] * x1 + w[2]
    mse = np.mean((y - t)**2)
    return mse

# 2次元の解析解
def fit_plane(x0, x1, t):
    c_tx0 = np.mean(t * x0) - np.mean(t) * np.mean(x0)      # tとx0の共分散
    c_tx1 = np.mean(t * x1) - np.mean(t) * np.mean(x1)      # tとx1の共分散
    c_x0x1 = np.mean(x0 * x1) - np.mean(x0) * np.mean(x1)   # x0とx1の共分散

    v_x0 = np.var(x0)
    v_x1 = np.var(x1)

    w0 = (c_tx1 * c_x0x1 - v_x1 * c_tx0) / (c_x0x1**2 - v_x0 * v_x1)
    w1 = (c_tx0 * c_x0x1 - v_x0 * c_tx1) / (c_x0x1**2 - v_x0 * v_x1)
    w2 = -w0 * np.mean(x0) - w1 * np.mean(x1) + np.mean(t)

    # return np.array([w0, w1, w2])
    return np.array([w0, w1, w2])
# print(np.round(X0, 2))
# print(np.round(X1, 2))
# print(np.round(T, 2))


# plt.figure(figsize=(6, 5))
# ax = plt.subplot(1,1,1,projection='3d')
# show_data2(ax, X0, X1, T)
# plt.show()

# plt.figure(figsize=(6, 5))
# ax = plt.subplot(1,1,1,projection='3d')
# W = [1.5, 1, 90]
# show_plane(ax, W)
# show_data2(ax, X0, X1, T)
# mse = mse_plane(X0, X1, T, W)
# print("SD={0:.2f}cm".format(np.sqrt(mse)))
# plt.show()

plt.figure(figsize=(6, 5))
ax = plt.subplot(1,1,1,projection='3d')
W = fit_plane(X0, X1, T)
print("W0={0:.1f}, W1={1:.1f}, W2={2:.1f}".format(W[0], W[1], W[2]))
show_plane(ax, W)
show_data2(ax, X0, X1, T)
mse = mse_plane(X0, X1, T, W)
print("SD={0:.2f}cm".format(np.sqrt(mse)))
plt.show()