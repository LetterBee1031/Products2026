import numpy as np
import matplotlib.pyplot as plt

x = np.linspace(-4, 4, 100)
y = 2**x
y2 = 3**x
y3 = 0.5**x

plt.figure(figsize=(5,5))
plt.plot(x, y, 'black', linewidth=3, label='$y=2^x$')
plt.plot(x, y2, 'cornflowerblue', linewidth=3, label='$y=3^x$')
plt.plot(x, y3, 'gray', linewidth=3, label='$y=0.5^x$')
plt.plot(-2, 6)
plt.plot(-4, 4)

plt.grid(True)
plt.legend(loc='lower right')
plt.show()