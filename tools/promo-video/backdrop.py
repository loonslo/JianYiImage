"""Full-screen dark backdrop that record.py shows while it drives the app.

It hides the desktop behind the recorded window and tells whoever is at the computer to keep off the mouse and
keyboard. record.py starts it as a separate process and ends it by PID.
"""

import ctypes
import sys
import tkinter as tk

ctypes.windll.shcore.SetProcessDpiAwareness(2)  # geometry in physical pixels
root = tk.Tk()
root.overrideredirect(True)
root.geometry(f"{root.winfo_screenwidth()}x{root.winfo_screenheight()}+0+0")
root.configure(bg="#0e0e10")
root.attributes("-topmost", True)
message = sys.argv[1] if len(sys.argv) > 1 else "正在录制，请勿操作鼠标和键盘"
tk.Label(root, text=message, fg="#d8d8d8", bg="#0e0e10", font=("Microsoft YaHei", 26), justify="left").place(x=70, y=110)
root.mainloop()
