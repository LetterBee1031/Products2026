import numpy as np
import matplotlib.pyplot as plt

def softmax(x0, x1, x2):
    u = np.exp(x0) + np.exp(x1) + np.exp(x2)
    return np.exp(x0) / u, np.exp(x1) / u, np.exp(x2) / u

y = softmax(21, 20, -12)   # ソフトマックス関数の計算を実行

print(np.round(y, 2))   # 各値を小数点以下2桁に丸めて表示
print(np.sum(y))        # 和を表示