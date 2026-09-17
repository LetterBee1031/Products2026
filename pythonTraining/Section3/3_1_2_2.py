import numpy as np
import matplotlib.pyplot as plt

def f(x):
    return (x - 2) * x * (x + 2)

def f2(x, w):
    return (x - w) * x * (x + 2)

x = np.linspace(-3, 3, 100) # -3 以上 3 以下を10分割

# グラフ描画
plt.plot(x, f2(x, 2), color='black', label='$w=2$')            #
plt.plot(x, f2(x, 1), color='cornflowerblue', label='$w=1$')   #
plt.legend(loc="upper left")    # 凡例
plt.ylim(-15, 15)               # y軸の範囲
plt.title('$f_2(x)$')           # タイトル
plt.xlabel('$x$')               # xラベル
plt.ylabel('$y$')               # yラベル
plt.grid(True)                  # グリッド
f
plt.show()