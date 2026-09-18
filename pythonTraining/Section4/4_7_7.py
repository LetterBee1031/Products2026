import numpy as np
import matplotlib.pyplot as plt

x = np.linspace(-10, 10, 100)
y = 1 / (1 + np.exp(-x))

plt.figure(figsize=(4,4))
plt.plot(x, y, color = 'black', linewidth=3)

plt.ylim(-1, 2)
plt.xlim(-10, 10)

plt.grid(True)
plt.show()