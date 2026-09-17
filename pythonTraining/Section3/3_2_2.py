import numpy as np
import matplotlib.pyplot as plt
from mpl_toolkits.mplot3d import Axes3D

# 関数f3を定義
def f3(x0, x1):
    ans = (2 * x0**2 + x1**2) * np.exp(-(2 * x0**2 + x1**2))
    return ans

# 各x0, x1でf3を計算
# xn = 9
xn = 50
x0 = np.linspace(-2, 2, xn)
x1 = np.linspace(-2, 2, xn)

xx0, xx1 = np.meshgrid(x0, x1) # 格子列を返す関数．動作を言葉で説明しづらい

y = np.zeros((len(x0), len(x1)))

for i0 in range(xn):
    for i1 in range(xn):
        y[i1, i0] = f3(x0[i0], x1[i1])

# print(np.round(y, 1))
print(xx0)
print(xx1)
# 描画
plt.figure(figsize=(5, 3.5))
ax = plt.subplot(1, 1, 1, projection='3d') #3次元の指定
ax.plot_surface(xx0, xx1, y, rstride=1, cstride=1, alpha=0.3, color='blue', edgecolor='black') # rstride, cstrideは何要素ごとに線を引くか，らしい．自然数のみ指定可
ax.set_zticks((0,0.2)) # z軸のメモリを0と0.2だけにした
ax.view_init(75, -95) # 図の回転角の指定
plt.show()