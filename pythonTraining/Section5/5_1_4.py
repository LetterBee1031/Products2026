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

# print(X)
# print(np.round(X, 2))
# print(np.round(T, 2))

# # データの表示
# plt.figure(figsize=(4, 4))
# plt.plot(X, T, marker='o', linestyle='None', markeredgecolor='black', color='cornflowerblue')
# plt.xlim(X_min, X_max)
# plt.grid(True)
# plt.show()

# 平均誤差関数
def mse_line(x, t, w):
    y = w[0] * x + w[1]
    mse = np.mean((y - t)**2)
    return mse

# 計算
# xn = 100
# w0_range = [-25, 25]
# w1_range = [120, 170]

# w0 = np.linspace(w0_range[0], w0_range[1], xn)
# w1 = np.linspace(w1_range[0], w1_range[1], xn)

# ww0, ww1 = np.meshgrid(w0, w1)
# J = np.zeros((len(w0), len(w1)))
# for i0 in range(len(w0)):
#     for i1 in range(len(w1)):
#         J[i1, i0] = mse_line(X, T, (w0[i0], w1[i1]))

# # 表示
# plt.figure(figsize=(9.5, 4))
# plt.subplots_adjust(wspace=0.5)
# ax = plt.subplot(1, 2, 1, projection='3d') #3次元の指定
# ax.plot_surface(ww0, ww1, J, rstride=10, cstride=10, alpha=0.3, color='blue', edgecolor='black') # rstride, cstrideは何要素ごとに線を引くか，らしい．自然数のみ指定可
# ax.set_xticks([-20, 0, 20])
# ax.set_yticks([120, 140, 160])
# ax.view_init(20, -60) # 図の回転角の指定

# plt.subplot(1, 2, 2)
# cont = plt.contour(ww0, ww1, J, 30, color='black', levels=[100, 1000, 10000, 100000])
# cont.clabel(fmt='%d', fontsize=8)
# plt.grid(True)
# plt.show()

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


d_w = dmse_line(X, T, [10, 165])
print(np.round(d_w, 1))

# メイン関数的な
# MSEの等高線表示
plt.figure(figsize=(4, 4))
wn = 100
w0_range = [-25, 25]
w1_range = [120, 170]

w0 = np.linspace(w0_range[0], w0_range[1], wn)
w1 = np.linspace(w1_range[0], w1_range[1], wn)
ww0, ww1 = np.meshgrid(w0, w1)
J = np.zeros((len(w0), len(w1)))

for i0 in range(wn):
    for i1 in range(wn):
        J[i1, i0] = mse_line(X, T, (w0[i0], w1[i1]))

cont = plt.contour(ww0, ww1, J, 30, colors='black', levels=(100, 1000, 10000, 100000))
cont.clabel(fmt='%d', fontsize=8)
plt.grid(True)

# 勾配法呼び出し
W0, W1, dMSE, w_history = fit_line_num(X, T)

# 結果表示
print(' 繰り返し回数 {0}'.format(w_history.shape[0]))            # 勾配降下の繰り返し回数
print('W=[{0:.6f}, {1:.6f}]'.format(W0, W1))                    # w_0, w_1の値
print('dMSE=[{0:.6f}, {1:.6f}]'.format(dMSE[0], dMSE[1]))       # dMSE 平均二乗誤差の勾配
print('MSE=[{0:.6f}]'.format(mse_line(X, T, [W0, W1])))         # MSE  平均二乗誤差
plt.plot(w_history[:, 0], w_history[:, 1], '.-', color='gray', markersize=10, markeredgecolor='cornflowerblue')

plt.figure(figsize=(4, 4))
W = np.array([W0, W1])
mse = mse_line(X, T, W)
print('W0={0:.3f}, W1={1:.3f}'.format(W0, W1))
print("SD={0:.3f} cm".format(np.sqrt(mse)))
show_line(W)
plt.plot(X, T, marker='o', linestyle='None', markeredgecolor='black', color='cornflowerblue')
plt.xlim(X_min, X_max)
plt.grid(True)
plt.show()