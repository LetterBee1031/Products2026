import numpy as np
import matplotlib.pyplot as plt

# 関数f3を定義
def f3(x0, x1):
    ans = (2 * x0**2 + x1**2) * np.exp(-(2 * x0**2 + x1**2))
    return ans

# 各x0, x1でf3を計算
xn = 9
x0 = np.linspace(-2, 2, xn)
x1 = np.linspace(-2, 2, xn)

y = np.zeros((len(x0), len(x1)))

for i0 in range(xn):
    for i1 in range(xn):
        y[i1, i0] = f3(x0[i0], x1[i1])

print(x0)
print(np.round(y, 1))

# 描画
plt.figure(figsize=(3.5, 3))
plt.gray() # カラースケールの設定
plt.pcolor(y) # カラースケールで描画
# plt.contour(y) # 等高線で描画
plt.colorbar() # カラーバーを横に表示
plt.show()
# plt.subplots_adjust(wspace=0.5, hspace=0.5)
# for i in range(6):
#     plt.subplot(2, 3, i+1)
#     plt.title(i+1)
#     plt.plot(x, f2(x, i), 'k')
#     plt.ylim(-20, 20)
#     plt.grid(True)
# plt.show()