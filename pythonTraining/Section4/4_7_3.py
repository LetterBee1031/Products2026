import numpy as np
import matplotlib.pyplot as plt

x = np.linspace(-4, 4, 100)
y = (x - 1)**2 + 2
logy = np.log(y)

plt.figure(figsize=(4,4))
plt.plot(x, y, 'black', linewidth=3)
plt.plot(x, logy, 'cornflowerblue', linewidth=3)

plt.yticks(range(-4,9,1))
plt.xticks(range(-4,5,1))
plt.ylim(-4, 8)
plt.xlim(-4, 4)

plt.grid(True)
plt.show()