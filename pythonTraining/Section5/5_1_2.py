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
xn = 100
w0_range = [-25, 25]
w1_range = [120, 170]

w0 = np.linspace(w0_range[0], w0_range[1], xn)
w1 = np.linspace(w1_range[0], w1_range[1], xn)

ww0, ww1 = np.meshgrid(w0, w1)
J = np.zeros((len(w0), len(w1)))
for i0 in range(len(w0)):
    for i1 in range(len(w1)):
        J[i1, i0] = mse_line(X, T, (w0[i0], w1[i1]))

# 表示
plt.figure(figsize=(9.5, 4))
plt.subplots_adjust(wspace=0.5)
ax = plt.subplot(1, 2, 1, projection='3d') #3次元の指定
ax.plot_surface(ww0, ww1, J, rstride=10, cstride=10, alpha=0.3, color='blue', edgecolor='black') # rstride, cstrideは何要素ごとに線を引くか，らしい．自然数のみ指定可
ax.set_xticks([-20, 0, 20])
ax.set_yticks([120, 140, 160])
ax.view_init(20, -60) # 図の回転角の指定

plt.subplot(1, 2, 2)
cont = plt.contour(ww0, ww1, J, 30, color='black', levels=[100, 1000, 10000, 100000])
cont.clabel(fmt='%d', fontsize=8)
plt.grid(True)
plt.show()