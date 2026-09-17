import numpy as np
import matplotlib.pyplot as plt

np.random.seed(1)
x = np.arange(10) # 0~9の配列を生成
y = np.random.rand(10) # ランダム値を10個生成

plt.plot(x, y) # グラフの登録
plt.show() # グラフ出力