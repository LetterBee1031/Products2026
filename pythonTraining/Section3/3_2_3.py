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

# 描画
plt.figure(1, figsize=(4, 4))
cont = plt.contour(xx0, xx1, y, 5, colors='black')
cont.clabel(fmt='%.2f', fontsize=8)
plt.xlabel('$x_0$', fontsize=14)
plt.ylabel('$x_1$', fontsize=14)
plt.show()