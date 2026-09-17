import numpy as np
import matplotlib.pyplot as plt
from mpl_toolkits.mplot3d import Axes3D

# 関数f
def f(w0, w1):
    return w0**2 + 2 * w0 * w1 + 3

# 関数fの w0での偏微分
def df_dw0(w0, w1):
    return 2 * w0 + 2 * w1

# 関数fの w0での偏微分
def df_dw1(w0, w1):
    return 2 * w0 + 0 * w1

w_range = 2
dw = 0.25
w0 = np.arange(-w_range, w_range+dw, dw)
w1 = np.arange(-w_range, w_range+dw, dw)

ww0, ww1 = np.meshgrid(w0, w1)

ff = np.zeros((len(w0), len(w1)))
dff_w0 = np.zeros((len(w0), len(w1)))
dff_w1 = np.zeros((len(w0), len(w1)))

for i0 in range(len(w0)):
    for i1 in range(len(w1)):
        ff[i1, i0] = f(w0[i0], w1[i1])
        dff_w0[i1, i0] = df_dw0(w0[i0], w1[i1])
        dff_w1[i1, i0] = df_dw1(w0[i0], w1[i1])

plt.figure(figsize=(9, 4))
plt.subplots_adjust(wspace=0.3)
plt.subplot(1, 2, 1)

cont = plt.contour(ww0, ww1, ff, 10, colors='k')
cont.clabel(fmt='%d', fontsize=8)

# 等高線表示
plt.xticks(range(-w_range, w_range+1, 1))
plt.yticks(range(-w_range, w_range+1, 1))
plt.xlim(-w_range-0.5, w_range+0.5)
plt.ylim(-w_range-0.5, w_range+0.5)
plt.xlabel('$w_0$', fontsize=14)
plt.ylabel('$w_1$', fontsize=14)

# 勾配ベクトル表示
plt.subplot(1, 2, 2)
plt.quiver(ww0, ww1, dff_w0, dff_w1)
plt.xlabel('$w_0$', fontsize=14)
plt.ylabel('$w_1$', fontsize=14)
plt.xticks(range(-w_range, w_range+1, 1))
plt.yticks(range(-w_range, w_range+1, 1))
plt.xlim(-w_range-0.5, w_range+0.5)
plt.ylim(-w_range-0.5, w_range+0.5)
plt.show()