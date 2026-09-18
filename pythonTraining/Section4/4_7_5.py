import numpy as np
import matplotlib.pyplot as plt

x = np.linspace(-4, 4, 100)
a = 2
y = a**x
dy = np.log(a) * y

plt.figure(figsize=(4,4))
plt.plot(x, y, 'gray', linestyle='--', linewidth=3)
plt.plot(x, dy, color = 'black', linewidth=3)

plt.ylim(-1, 8)
plt.xlim(-4, 4)

plt.grid(True)
plt.show()