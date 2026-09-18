import numpy as np
import matplotlib.pyplot as plt

x = np.linspace(0.0001, 4, 100)
y = np.log(x)
dy = 1 / x

plt.figure(figsize=(4,4))
plt.plot(x, y, 'gray', linestyle='--', linewidth=3)
plt.plot(x, dy, color = 'black', linewidth=3)

plt.ylim(-8, 8)
plt.xlim(-1, 4)

plt.grid(True)
plt.show()