import numpy as np
import matplotlib.pyplot as plt

def f(x):
    return (x - 2) * x * (x + 2)

print(f(1))
print(f(np.array([1,2,3])))

x = np.arange(-3, 3.5, 0.5) #-3 以上 3.5 未満を0.5刻みで
print(x)

x = np.linspace(-3, 3, 10) # -3 以上 3 以下を10分割
print(np.round(x, 2))

plt.plot(x, f(x))
plt.show()