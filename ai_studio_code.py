#!/usr/bin/env python3
"""
ZTE MF791D tw_telnet_config 简易控制面板。

功能：
- 自动/手动获取 IMEI
- 开启 Telnet
- 开启 Telnet + ADB
- 关闭 Telnet
- 重启设备
"""

from __future__ import annotations

import ctypes
import hashlib
import os
import re
import socket
import sys
import threading
import time
import tkinter as tk
from pathlib import Path
from typing import Callable, Optional

try:
    import customtkinter as ctk
except ImportError as exc:
    raise SystemExit("[!] 缺少 customtkinter: pip install customtkinter") from exc

try:
    from Crypto.Cipher import AES
except ImportError as exc:
    raise SystemExit("[!] 缺少 pycryptodome: pip install pycryptodome") from exc

try:
    import requests
    from requests import RequestException

    requests.packages.urllib3.disable_warnings()
except ImportError as exc:
    raise SystemExit("[!] 缺少 requests: pip install requests") from exc


APP_ID = "my.zte.tool.v1"
APP_TITLE = "开启Debug调试工具 - 金恩出品"
DEFAULT_HOST = "192.168.0.1"
DEFAULT_REMO_HOST = "192.168.100.1"
WINDOW_SIZE = "335x555"
FETCH_BUTTON_WIDTH = 68
FIELD_PADX = 10
FIELD_PADY = (0, 4)
CONTROL_HEIGHT = 28

AES_KEY = bytes.fromhex("9d4d6f47f025c03a3838f2796d8a43e3")
PAYLOAD_LEN = 128

GET_TIMEOUT = 3
POST_TIMEOUT = 8
XXREMO_TIMEOUT = 10

GOFORM_GET = "/goform/goform_get_cmd_process"
GOFORM_SET = "/goform/goform_set_cmd_process"
XXREMO_POST = "/reqproc/proc_post"

COLORS = {
    "bg": ("#f4f6fb", "#13161e"),
    "bg2": ("#ffffff", "#1a1e28"),
    "accent": ("#008f68", "#00e5a0"),
    "red": ("#d92d3a", "#ff5555"),
    "amber": ("#b77900", "#f5a623"),
    "text": ("#111827", "#e8ecf4"),
    "text2": ("#64748b", "#7a8299"),
    "border": ("#d8dee9", "#1e2330"),
}

FONT_FAMILY = "Microsoft YaHei UI"
FONTS = {
    "title": (FONT_FAMILY, 15, "bold"),
    "section": (FONT_FAMILY, 13, "bold"),
    "label": (FONT_FAMILY, 12),
    "entry": (FONT_FAMILY, 12),
    "button": (FONT_FAMILY, 12),
    "button_small": (FONT_FAMILY, 14, "bold"),
    "info": (FONT_FAMILY, 11),
    "dialog_title": (FONT_FAMILY, 13, "bold"),
    "dialog_text": (FONT_FAMILY, 12),
    "dialog_button": (FONT_FAMILY, 12),
    "dialog_button_bold": (FONT_FAMILY, 12, "bold"),
}


def resource_path(relative_path: str) -> str:
    """获取运行时资源路径，兼容 PyInstaller。"""
    base_dir = getattr(sys, "_MEIPASS", None)
    if base_dir:
        return str(Path(base_dir) / relative_path)
    return str(Path(__file__).resolve().parent / relative_path)


def normalize_host(host: str) -> str:
    """允许用户输入 IP、host 或带协议的地址。"""
    value = host.strip()
    value = value.removeprefix("http://").removeprefix("https://").rstrip("/")
    if not value:
        raise ValueError("请输入路由器 IP")
    return value


def build_params(plaintext: str) -> tuple[str, str]:
    """生成 tw_telnet_config 需要的 AES 参数和 MD5 校验值。"""
    payload = plaintext.encode("utf-8")
    if len(payload) >= PAYLOAD_LEN:
        raise ValueError(f"明文过长，最多 {PAYLOAD_LEN - 1} 字节")

    padded = payload + b"\x00" * (PAYLOAD_LEN - len(payload))
    params = AES.new(AES_KEY, AES.MODE_ECB).encrypt(padded).hex().upper()
    md5_check = hashlib.md5(payload).hexdigest().upper()
    return params, md5_check


def extract_response_field(key: str, text: str) -> str:
    """从非标准 JSON 响应中提取字段值。"""
    patterns = (
        r'"' + re.escape(key) + r'"\s*:\s*"([^"]*)"',
        r"'" + re.escape(key) + r"'\s*:\s*'([^']*)'",
        r'"' + re.escape(key) + r'"\s*:\s*([^,}\s]+)',
        r"\b" + re.escape(key) + r"\s*=\s*([^&\s,}]+)",
    )
    for pattern in patterns:
        match = re.search(pattern, text, re.IGNORECASE)
        if match:
            return match.group(1).strip().strip('"').strip("'")
    return ""


def short_response(text: str, limit: int = 80) -> str:
    value = " ".join(text.strip().split())
    if len(value) > limit:
        return value[:limit] + "..."
    return value


def xxremo_key_fang(imei: str, ts: str, version: str) -> str:
    seed = f"fang{imei}po{ts}jie{version}666"
    return hashlib.md5(seed.encode()).hexdigest().upper()


def xxremo_key_xinxun(imei: str, ts: str, version: str) -> str:
    seed = f"xinxun8888{imei}{ts}{version}xinxun6666"
    return hashlib.md5(seed.encode()).hexdigest().upper()


def xxremo_key_zk(imei: str, ts: str, version: str) -> str:
    seed = f"zk333{ts}{imei}{version}zk444"
    return hashlib.md5(seed.encode()).hexdigest().upper()


XXREMO_ALGOS: tuple[tuple[str, Callable[[str, str, str], str]], ...] = (
    ("fang", xxremo_key_fang),
    ("xinxun", xxremo_key_xinxun),
    ("zk", xxremo_key_zk),
)


class RouterClient:
    """封装路由器 HTTP 接口，避免 GUI 层散落请求细节。"""

    def __init__(self, host: str):
        self.host = normalize_host(host)
        self.base_url = f"http://{self.host}"
        self.session = requests.Session()

    def fetch_imei(self) -> str:
        response = self.session.get(
            f"{self.base_url}{GOFORM_GET}",
            params={"cmd": "imei"},
            timeout=GET_TIMEOUT,
        )
        response.raise_for_status()
        return str(response.json().get("imei", "")).strip()

    def send_payload(self, plaintext: str) -> bool:
        params, md5_check = build_params(plaintext)
        response = self.session.post(
            f"{self.base_url}{GOFORM_SET}",
            data={
                "goformId": "tw_telnet_config",
                "params": params,
                "md5_check": md5_check,
            },
            timeout=POST_TIMEOUT,
        )
        response.raise_for_status()
        return "pass" in response.text.lower()

    def _socket_target(self) -> tuple[str, int]:
        host = self.host
        port = 80
        if host.count(":") == 1:
            name, raw_port = host.rsplit(":", 1)
            if raw_port.isdigit():
                host = name
                port = int(raw_port)
        return host, port

    def send_xxremo_post(self, fields: dict[str, str]) -> str:
        """使用 raw socket 发送 XXREMO POST，兼容设备提前 RST 的情况。"""
        body = "&".join(f"{key}={value}" for key, value in fields.items()).encode()
        request = (
            f"POST {XXREMO_POST} HTTP/1.0\r\n"
            f"Host: {self.host}\r\n"
            f"Content-Type: application/x-www-form-urlencoded\r\n"
            f"Content-Length: {len(body)}\r\n"
            "Connection: close\r\n"
            "\r\n"
        ).encode("utf-8") + body

        raw = b""
        sock: Optional[socket.socket] = None
        try:
            sock = socket.socket()
            sock.settimeout(XXREMO_TIMEOUT)
            sock.connect(self._socket_target())
            sock.sendall(request)
            while True:
                chunk = sock.recv(4096)
                if not chunk:
                    break
                raw += chunk
        except OSError:
            pass
        finally:
            if sock is not None:
                try:
                    sock.close()
                except OSError:
                    pass

        if b"\r\n\r\n" in raw:
            raw = raw.split(b"\r\n\r\n", 1)[1]
        return raw.decode(errors="replace")

    def fetch_xxremo_debug_info(self) -> tuple[str, str, str, str]:
        raw_info = self.send_xxremo_post({"goformId": "Getdebuginfo"})
        if not raw_info.strip():
            raise ValueError("无法连接设备或获取响应")

        imei = extract_response_field("imei", raw_info)
        version = extract_response_field("version", raw_info)
        ts = extract_response_field("debug_info", raw_info)
        return imei, version, ts, raw_info.strip()

    def reboot_xxremo_device(self) -> bool:
        self.send_xxremo_post({"isTest": "false", "goformId": "REBOOT_DEVICE"})
        return True

    def enable_xxremo_debug(
        self,
        progress: Optional[Callable[[str], None]] = None,
    ) -> tuple[bool, str, str]:
        imei, version, ts, _raw_info = self.fetch_xxremo_debug_info()
        if not all([imei, version, ts]):
            return False, "REMO 关键字段缺失，无法生成 Key", imei

        if progress:
            progress(f"REMO 信息已获取: IMEI {imei}")

        for name, make_key in XXREMO_ALGOS:
            key = make_key(imei, ts, version)
            if progress:
                progress(f"正在尝试 REMO {name} 算法...")

            result = self.send_xxremo_post(
                {
                    "goformId": "SysCtlUtal",
                    "action": "System_MODE",
                    "debug_enable": "1",
                    "key": key,
                }
            )
            compact = result.replace(" ", "")
            if '"result":"0"' in compact or "successfully" in result.lower():
                return True, f"REMO Debug 开启成功: {name}", imei

        return False, "REMO Debug 开启失败，已知算法均未生效", imei


class App(ctk.CTk):
    def __init__(self) -> None:
        ctk.set_appearance_mode("system")
        super().__init__()

        self.C = COLORS
        self._busy = False
        self._buttons: list[ctk.CTkButton] = []
        self._dialog_overlay: Optional[ctk.CTkFrame] = None

        self._init_windows_app_id()
        self._init_window()
        self._build_ui()

        self.after(500, self._auto_fetch_device_info)

    def _init_windows_app_id(self) -> None:
        if os.name != "nt":
            return
        try:
            ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID(APP_ID)
        except Exception:
            pass

    def _init_window(self) -> None:
        self.title(APP_TITLE)
        self.geometry(WINDOW_SIZE)
        self.resizable(False, False)
        self.configure(fg_color=self.C["bg"])

        icon_path = resource_path("logo.ico")
        if os.path.exists(icon_path):
            try:
                self.iconbitmap(icon_path)
            except Exception as exc:
                print(f"图标加载失败: {exc}")

    def _build_ui(self) -> None:
        main_frame = ctk.CTkFrame(self, fg_color=self.C["bg"], corner_radius=0)
        main_frame.pack(fill="both", expand=True, padx=16)

        self._section_title(main_frame, "新版 F32/F30PRO", FONTS["title"], pady=(6, 5))

        self._label(main_frame, "IP")
        self.var_host = tk.StringVar(value=DEFAULT_HOST)
        self.host_entry = self._entry(main_frame, self.var_host)
        self.host_entry.pack(fill="x", padx=FIELD_PADX, pady=FIELD_PADY)
        self.host_entry.bind("<Return>", lambda _event: self._fetch_imei())

        self._label(main_frame, "IMEI")
        imei_row = ctk.CTkFrame(main_frame, fg_color=self.C["bg"], corner_radius=0)
        imei_row.pack(fill="x", padx=FIELD_PADX, pady=FIELD_PADY)

        self.var_imei = tk.StringVar()
        self.imei_entry = self._entry(imei_row, self.var_imei)
        self.imei_entry.pack(side="left", fill="x", expand=True)

        self._button(
            imei_row,
            "获取",
            self._fetch_imei,
            self.C["bg"],
            bg=self.C["accent"],
            font=FONTS["button_small"],
            side="right",
            padx=(6, 0),
            fill=None,
            pady=0,
        )

        self._button(main_frame, "▶  开启 Telnet", self._do_telnet, self.C["accent"])
        self._button(main_frame, "★  开启 Telnet + Debug", self._do_all, self.C["amber"])
        self._button(main_frame, "■  关闭 Telnet", self._do_disable, self.C["text2"])

        self._button(main_frame, "↻  立即重启设备", self._do_reboot, self.C["red"])

        self._separator(main_frame)
        self._section_title(main_frame, "REMO", FONTS["section"], pady=(4, 5))

        self._label(main_frame, "IP")
        self.var_remo_host = tk.StringVar(value=DEFAULT_REMO_HOST)
        self.remo_host_entry = self._entry(main_frame, self.var_remo_host)
        self.remo_host_entry.pack(fill="x", padx=FIELD_PADX, pady=FIELD_PADY)
        self.remo_host_entry.bind("<Return>", lambda _event: self._do_xxremo_debug())

        self._label(main_frame, "IMEI")
        remo_imei_row = ctk.CTkFrame(main_frame, fg_color=self.C["bg"], corner_radius=0)
        remo_imei_row.pack(fill="x", padx=FIELD_PADX, pady=FIELD_PADY)

        self.var_remo_imei = tk.StringVar()
        self.remo_imei_entry = self._entry(remo_imei_row, self.var_remo_imei)
        self.remo_imei_entry.pack(side="left", fill="x", expand=True)

        self._button(
            remo_imei_row,
            "获取",
            self._fetch_remo_imei,
            self.C["bg"],
            bg=self.C["accent"],
            font=FONTS["button_small"],
            side="right",
            padx=(6, 0),
            fill=None,
            pady=0,
        )

        self._button(main_frame, "★  开启 REMO Debug", self._do_xxremo_debug, self.C["amber"])
        self._button(main_frame, "↻  立即重启设备", self._do_remo_reboot, self.C["red"])

        self.lbl_info = ctk.CTkLabel(
            self,
            text="就绪",
            fg_color=self.C["bg2"],
            text_color=self.C["text2"],
            font=FONTS["info"],
            height=25,
        )
        self.lbl_info.pack(side="bottom", fill="x")

    def _section_title(
        self,
        parent: ctk.CTkFrame,
        text: str,
        font: tuple[str, int] | tuple[str, int, str],
        pady: tuple[int, int],
    ) -> None:
        row = ctk.CTkFrame(parent, fg_color="transparent", corner_radius=0)
        row.pack(fill="x", pady=pady)
        row.grid_columnconfigure(0, weight=1, uniform="section")
        row.grid_columnconfigure(1, weight=0)
        row.grid_columnconfigure(2, weight=1, uniform="section")

        ctk.CTkFrame(row, fg_color=self.C["border"], height=1, corner_radius=0).grid(
            row=0,
            column=0,
            sticky="ew",
            padx=(0, 10),
        )
        ctk.CTkLabel(
            row,
            text=text,
            fg_color="transparent",
            text_color=self.C["accent"],
            font=font,
        ).grid(row=0, column=1)
        ctk.CTkFrame(row, fg_color=self.C["border"], height=1, corner_radius=0).grid(
            row=0,
            column=2,
            sticky="ew",
            padx=(10, 0),
        )

    def _label(self, parent: ctk.CTkFrame, text: str) -> None:
        ctk.CTkLabel(
            parent,
            text=text,
            fg_color="transparent",
            text_color=self.C["text2"],
            font=FONTS["label"],
        ).pack(anchor="w")

    def _entry(self, parent: ctk.CTkFrame, textvariable: tk.StringVar) -> ctk.CTkEntry:
        return ctk.CTkEntry(
            parent,
            textvariable=textvariable,
            fg_color=self.C["bg2"],
            text_color=self.C["text"],
            border_color=self.C["border"],
            border_width=1,
            corner_radius=6,
            font=FONTS["entry"],
            height=CONTROL_HEIGHT,
        )

    def _button(
        self,
        parent: ctk.CTkFrame,
        text: str,
        command: Callable[[], None],
        fg: str,
        *,
        bg: Optional[str] = None,
        font: tuple[str, int] | tuple[str, int, str] = FONTS["button"],
        side: Optional[str] = None,
        padx: int | tuple[int, int] = 10,
        pady: int | tuple[int, int] = 2,
        fill: Optional[str] = "x",
    ) -> ctk.CTkButton:
        button = ctk.CTkButton(
            parent,
            text=text,
            command=command,
            fg_color=bg or self.C["bg2"],
            hover_color=self.C["border"],
            text_color=fg,
            font=font,
            cursor="hand2",
            anchor="center" if fill is None else "w",
            corner_radius=6,
            width=FETCH_BUTTON_WIDTH if fill is None else 0,
            height=CONTROL_HEIGHT,
        )
        pack_kwargs = {"pady": pady}
        if side:
            pack_kwargs["side"] = side
        if padx is not None:
            pack_kwargs["padx"] = padx
        if fill:
            pack_kwargs["fill"] = fill
        button.pack(**pack_kwargs)
        self._buttons.append(button)
        return button

    def _separator(self, parent: ctk.CTkFrame) -> None:
        ctk.CTkFrame(parent, fg_color=self.C["border"], height=1.5, corner_radius=0).pack(fill="x", pady=4)

    def _ask_in_window(self, title: str, message: str) -> bool:
        if self._dialog_overlay is not None:
            return False

        result = tk.StringVar(value="")
        overlay = ctk.CTkFrame(self, fg_color=self.C["bg"], corner_radius=0)
        self._dialog_overlay = overlay
        overlay.place(x=0, y=0, relwidth=1, relheight=1)
        overlay.lift()
        overlay.grab_set()

        panel = ctk.CTkFrame(
            overlay,
            fg_color=self.C["bg2"],
            border_color=self.C["border"],
            border_width=1,
            corner_radius=8,
            width=280,
        )
        panel.place(relx=0.5, rely=0.5, anchor="center")

        ctk.CTkLabel(
            panel,
            text=title,
            fg_color="transparent",
            text_color=self.C["text"],
            font=FONTS["dialog_title"],
        ).pack(anchor="w", padx=18, pady=(16, 10))

        ctk.CTkLabel(
            panel,
            text=message,
            fg_color="transparent",
            text_color=self.C["text2"],
            font=FONTS["dialog_text"],
            justify="left",
            wraplength=240,
        ).pack(anchor="w", fill="x", padx=18)

        button_row = ctk.CTkFrame(panel, fg_color=self.C["bg2"], corner_radius=0)
        button_row.pack(fill="x", padx=18, pady=(18, 16))

        def close(choice: bool) -> None:
            result.set("yes" if choice else "no")
            overlay.grab_release()
            overlay.destroy()
            self._dialog_overlay = None

        ctk.CTkButton(
            button_row,
            text="否",
            command=lambda: close(False),
            fg_color=self.C["border"],
            hover_color=self.C["bg"],
            text_color=self.C["text2"],
            font=FONTS["dialog_button"],
            width=68,
            height=32,
            corner_radius=6,
        ).pack(side="right")
        ctk.CTkButton(
            button_row,
            text="是",
            command=lambda: close(True),
            fg_color=self.C["accent"],
            hover_color=self.C["amber"],
            text_color=self.C["bg"],
            font=FONTS["dialog_button_bold"],
            width=68,
            height=32,
            corner_radius=6,
        ).pack(side="right", padx=(0, 8))

        overlay.bind("<Escape>", lambda _event: close(False))
        self.wait_variable(result)
        return result.get() == "yes"

    def _make_client(self) -> Optional[RouterClient]:
        try:
            client = RouterClient(self.var_host.get())
        except ValueError as exc:
            self._update_info(str(exc), self.C["red"])
            return None

        if client.host != self.var_host.get().strip():
            self.var_host.set(client.host)
        return client

    def _make_remo_client(self) -> Optional[RouterClient]:
        try:
            client = RouterClient(self.var_remo_host.get())
        except ValueError as exc:
            self._update_info(str(exc), self.C["red"])
            return None

        if client.host != self.var_remo_host.get().strip():
            self.var_remo_host.set(client.host)
        return client

    def _set_busy(self, busy: bool) -> None:
        self._busy = busy
        state = "disabled" if busy else "normal"
        for button in self._buttons:
            button.configure(state=state)

    def _update_info(self, text: str, color: Optional[str] = None) -> None:
        self.lbl_info.configure(text=text, text_color=color or self.C["text2"])

    def _run_worker(
        self,
        client: RouterClient,
        start_message: str,
        work: Callable[[RouterClient], None],
        on_done: Optional[Callable[[], None]] = None,
    ) -> None:
        if self._busy:
            return

        self._set_busy(True)
        self._update_info(start_message)

        def runner() -> None:
            try:
                work(client)
            finally:
                def finish() -> None:
                    self._set_busy(False)
                    if on_done:
                        on_done()

                self.after(0, finish)

        threading.Thread(target=runner, daemon=True).start()

    def _post_ui(self, callback: Callable[[], None]) -> None:
        self.after(0, callback)

    def _post_info(self, text: str, color: Optional[str] = None) -> None:
        self._post_ui(lambda: self._update_info(text, color))

    def _set_imei(self, imei: str, message: str, color: str) -> None:
        self.var_imei.set(imei)
        self._update_info(message, color)

    def _set_remo_imei(self, imei: str, message: str, color: str) -> None:
        self.var_remo_imei.set(imei)
        self._update_info(message, color)

    def _auto_fetch_device_info(self) -> None:
        if self._busy:
            return

        self._set_busy(True)
        self._update_info("正在自动获取设备信息...")

        def runner() -> None:
            result = {"f32_imei": "", "remo_imei": "", "remo_error": ""}

            def fetch_f32() -> None:
                try:
                    f32_client = RouterClient(self.var_host.get())
                    result["f32_imei"] = f32_client.fetch_imei()
                    self._post_ui(lambda host=f32_client.host: self.var_host.set(host))
                except (RequestException, ValueError):
                    pass

            def fetch_remo() -> None:
                try:
                    remo_client = RouterClient(self.var_remo_host.get())
                    imei, _version, _ts, raw_info = remo_client.fetch_xxremo_debug_info()
                    result["remo_imei"] = imei
                    self._post_ui(lambda host=remo_client.host: self.var_remo_host.set(host))
                    if not imei:
                        result["remo_error"] = f"REMO 未解析到 IMEI: {short_response(raw_info)}"
                except ValueError as exc:
                    result["remo_error"] = f"REMO 获取失败: {exc}"

            workers = [
                threading.Thread(target=fetch_f32, daemon=True),
                threading.Thread(target=fetch_remo, daemon=True),
            ]
            for worker in workers:
                worker.start()
            for worker in workers:
                worker.join()

            def finish() -> None:
                self._set_busy(False)
                f32_imei = result["f32_imei"]
                remo_imei = result["remo_imei"]
                remo_error = result["remo_error"]

                if f32_imei:
                    self.var_imei.set(f32_imei)
                if remo_imei:
                    self.var_remo_imei.set(remo_imei)

                if f32_imei and remo_imei:
                    self._update_info(f"F32/F30BPRO: {f32_imei} / REMO: {remo_imei}", self.C["accent"])
                elif f32_imei:
                    self._update_info(f"IMEI 获取成功: {f32_imei}", self.C["accent"])
                elif remo_imei:
                    self._update_info(f"REMO IMEI 获取成功: {remo_imei}", self.C["accent"])
                elif remo_error:
                    self._update_info(remo_error, self.C["red"])
                else:
                    self._update_info("未自动获取到设备 IMEI", self.C["text2"])

            self.after(0, finish)

        threading.Thread(target=runner, daemon=True).start()

    def _fetch_imei(self) -> None:
        client = self._make_client()
        if not client:
            return

        def work(router: RouterClient) -> None:
            try:
                imei = router.fetch_imei()
            except RequestException:
                self._post_info(f"无法连接到设备: {router.host}", self.C["text2"])
                return
            except ValueError:
                self._post_info("获取失败: 接口返回数据异常", self.C["red"])
                return

            if not imei:
                self._post_info("获取失败: 接口未返回 IMEI", self.C["red"])
                return

            self._post_ui(
                lambda: self._set_imei(
                    imei,
                    f"IMEI 获取成功: {imei}",
                    self.C["accent"],
                )
            )

        self._run_worker(client, "正在获取 IMEI...", work)

    def _fetch_remo_imei(self) -> None:
        client = self._make_remo_client()
        if not client:
            return

        def work(router: RouterClient) -> None:
            try:
                imei, _version, _ts, raw_info = router.fetch_xxremo_debug_info()
            except ValueError as exc:
                self._post_info(f"REMO 获取失败: {exc}", self.C["red"])
                return

            if not imei:
                self._post_info(f"REMO 未解析到 IMEI: {short_response(raw_info)}", self.C["red"])
                return

            self._post_ui(
                lambda: self._set_remo_imei(
                    imei,
                    f"REMO IMEI 获取成功: {imei}",
                    self.C["accent"],
                )
            )

        self._run_worker(client, "正在获取 REMO IMEI...", work)

    def _send_with_optional_imei(
        self,
        router: RouterClient,
        command: str,
        imei: str,
    ) -> bool:
        suffix = f"&imei={imei}" if imei else ""
        return router.send_payload(f"{command}{suffix}")

    def _resolve_imei(self, router: RouterClient, initial_imei: str) -> str:
        imei = initial_imei.strip()
        if imei:
            return imei

        self._post_info("缺失 IMEI，正在尝试补全...")
        try:
            imei = router.fetch_imei()
        except (RequestException, ValueError):
            return ""

        if imei:
            self._post_ui(
                lambda: self._set_imei(
                    imei,
                    f"已补全 IMEI: {imei}",
                    self.C["accent"],
                )
            )
        return imei

    def _do_telnet(self) -> None:
        self._execute_task(
            "telnetd_enable=1",
            "Telnet 开启成功",
            "确定开启 Telnet 吗？",
        )

    def _do_all(self) -> None:
        self._execute_task(
            "telnetd_enable=1&debug_enable=1",
            "Telnet & ADB 开启成功",
            "确定开启 Telnet + Debug 吗？",
        )

    def _do_xxremo_debug(self) -> None:
        if self._busy:
            return
        if not self._ask_in_window("确认", "确定开启 REMO Debug 吗？"):
            return

        client = self._make_remo_client()
        if not client:
            return

        result = {"ok": False, "message": "", "imei": ""}

        def work(router: RouterClient) -> None:
            try:
                ok, message, imei = router.enable_xxremo_debug(self._post_info)
            except ValueError as exc:
                ok = False
                message = str(exc)
                imei = ""
            except OSError:
                ok = False
                message = "REMO Debug 开启失败，请检查 IP 或连接"
                imei = ""

            result["ok"] = ok
            result["message"] = message
            result["imei"] = imei

        def done() -> None:
            if result["imei"]:
                self.var_remo_imei.set(result["imei"])

            if result["ok"]:
                self._update_info(result["message"], self.C["accent"])
            else:
                self._update_info(result["message"], self.C["red"])

        self._run_worker(client, "正在开启 REMO Debug...", work, done)

    def _do_remo_reboot(self) -> None:
        if self._busy:
            return
        if not self._ask_in_window("确认", "确定立即重启 REMO 设备吗？"):
            return

        client = self._make_remo_client()
        if not client:
            return

        result = {"ok": False}

        def work(router: RouterClient) -> None:
            try:
                result["ok"] = router.reboot_xxremo_device()
            except (OSError, ValueError):
                result["ok"] = False

        def done() -> None:
            if result["ok"]:
                self._update_info("REMO 重启指令已发送", self.C["amber"])
            else:
                self._update_info("REMO 重启指令发送失败，请检查 IP 或连接", self.C["red"])

        self._run_worker(client, "正在发送 REMO 重启指令...", work, done)

    def _do_disable(self) -> None:
        self._execute_task(
            "telnetd_enable=0",
            "Telnet 已关闭",
            "确定关闭 Telnet 吗？",
        )

    def _execute_task(self, command: str, success_msg: str, confirm_msg: str) -> None:
        if self._busy:
            return
        if not self._ask_in_window("确认", confirm_msg):
            return

        client = self._make_client()
        if not client:
            return

        initial_imei = self.var_imei.get()
        result = {"ok": False}

        def work(router: RouterClient) -> None:
            imei = self._resolve_imei(router, initial_imei)
            try:
                if command != "telnetd_enable=0":
                    self._send_with_optional_imei(router, "telnetd_enable=0", imei)
                    time.sleep(0.5)

                ok = self._send_with_optional_imei(router, command, imei)
            except (RequestException, ValueError):
                ok = False

            result["ok"] = ok

        def done() -> None:
            if result["ok"]:
                self._update_info(success_msg, self.C["accent"])
            else:
                self._update_info("指令执行失败，请检查 IP、连接或 IMEI", self.C["red"])

        self._run_worker(client, "正在执行指令...", work, done)

    def _do_reboot(self) -> None:
        self._send_reboot(confirm=True)

    def _send_reboot(self, *, confirm: bool) -> None:
        if self._busy:
            return
        if confirm and not self._ask_in_window("确认", "确定立即重启设备吗？"):
            return

        client = self._make_client()
        if not client:
            return

        initial_imei = self.var_imei.get()

        def work(router: RouterClient) -> None:
            imei = self._resolve_imei(router, initial_imei)
            try:
                ok = self._send_with_optional_imei(router, "reboot_now=1", imei)
            except (RequestException, ValueError):
                ok = False

            if ok:
                self._post_info("重启指令已发送", self.C["amber"])
            else:
                self._post_info("重启指令发送失败，请检查 IP、连接或 IMEI", self.C["red"])

        self._run_worker(client, "正在发送重启指令...", work)

if __name__ == "__main__":
    App().mainloop()
