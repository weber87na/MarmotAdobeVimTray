## En
Every time I opened Adobe PDF Reader, the existing "Vim mode" would reset or break, which was incredibly frustrating. So, I did some "vibe coding" and built this tool. It uses global keyboard hooks to ensure the Vim experience works exactly as intended.

Keybindings
h / l : Switch to Left / Right tab

j / k : Scroll Down / Up

d / u : Page Down / Up

gg / G : Jump to Top / Bottom

gt / gT : Next / Previous tab

:q : Close current tab; if only one tab remains, it exits Adobe PDF Reader entirely.

Usage & Features
System Tray: A Marmot icon will appear in the system tray (bottom-right corner) once the program is running.

Auto-start at Boot: Press Win + R, type shell:startup, and place a shortcut of the program into that folder.

## 中文
每次開 adobe pdf reader 本來的 vim 模式就會中斷有種火大的感覺, 所以 vibe coding 做了這個東西, 他靠全局攔截所以可以達到想要的效果

`h / l` : 左右切換分頁

`j / k` : 下/上捲動

`d / u` : 下/上翻頁

`gg / G` : 跳至首/尾

`gt / gT` : 切換下/上分頁

`:q` : 關閉目前分頁, 若剩下一個 tab 直接關閉整個 adobe pdf reader

開啟程式後右下角會出現土撥鼠

想要開機啟動 `win + r` => `shell:startup` 放入程式捷徑即可