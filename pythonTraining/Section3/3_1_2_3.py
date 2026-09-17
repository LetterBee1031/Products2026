import numpy as np
import matplotlib.pyplot as plt

def f(x):
    return (x - 2) * x * (x + 2)

def f2(x, w):
    return (x - w) * x * (x + 2)

x = np.linspace(-3, 3, 100) # -3 以上 3 以下を10分割

plt.figure(figsize=(10, 3))
plt.subplots_adjust(wspace=0.5, hspace=0.5)
for i in range(6):
    plt.subplot(2, 3, i+1)
    plt.title(i+1)
    plt.plot(x, f2(x, i), 'k')
    plt.ylim(-20, 20)
    plt.grid(True)
plt.show()