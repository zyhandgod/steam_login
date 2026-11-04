import React, { useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { createRoot } from "react-dom/client";
import { Provider as MotionProvider } from "rc-motion";
import {
  Alert,
  App as AntApp,
  Button,
  Checkbox,
  ConfigProvider,
  Drawer,
  Dropdown,
  Empty,
  Form,
  Input,
  InputNumber,
  Modal,
  Popconfirm,
  Progress,
  Segmented,
  Space,
  Switch,
  Table,
  Tag,
  Tooltip,
  Typography,
} from "antd";
import zhCN from "antd/locale/zh_CN";
import {
  AppstoreOutlined,
  CheckCircleFilled,
  DesktopOutlined,
  CloudSyncOutlined,
  DeleteOutlined,
  EditOutlined,
  ImportOutlined,
  PlusOutlined,
  ReloadOutlined,
  SearchOutlined,
  SettingOutlined,
} from "@ant-design/icons";
import "./style.css";
import appLogo from "../Assets/SteamSwitcher.png";
const { Text } = Typography;
const stamp = (value) =>
  value
    ? new Date(value).toLocaleString("zh-CN", {
        month: "2-digit",
        day: "2-digit",
        hour: "2-digit",
        minute: "2-digit",
        hour12: false,
      })
    : "—";
const effective = (a) => a.GameId?.trim() || a.Username;
async function api(command, payload) {
  if (!window.steam)
    throw new Error("请通过 Electron 打开工具，浏览器页面无法访问本机账号。");
  const r = await window.steam.request(command, payload);
  if (!r.ok) throw new Error(r.error);
  return r.value;
}
function Manager({ motionMode, setMotionMode }) {
  const { message, modal } = AntApp.useApp();
  const [data, setData] = useState({
    Accounts: [],
    Settings: {},
    CurrentAccountId: "",
  });
  const [loading, setLoading] = useState(true),
    [busy, setBusy] = useState(false),
    [fatal, setFatal] = useState("");
  const [env, setEnv] = useState({}),
    [search, setSearch] = useState(""),
    [status, setStatus] = useState("全部账号");
  const [selected, setSelected] = useState([]),
    [page, setPage] = useState(1),
    [pageSize, setPageSize] = useState(20),
    [query, setQuery] = useState(null);
  const tableRegion = useRef(null);
  const [tableHeight, setTableHeight] = useState(200);
  const [edit, setEdit] = useState(null),
    [importing, setImporting] = useState(false),
    [importText, setImportText] = useState(""),
    [settings, setSettings] = useState(false);
  const [form] = Form.useForm(),
    [settingsForm] = Form.useForm();
  async function run(task) {
    setBusy(true);
    try {
      return await task();
    } catch (e) {
      message.error(e.message, 6);
    } finally {
      setBusy(false);
    }
  }
  async function load() {
    setLoading(true);
    setFatal("");
    try {
      const e = await api("environment");
      setEnv(e);
      const r = await api("load");
      setData(r.data);
    } catch (e) {
      setFatal(e.message);
    } finally {
      setLoading(false);
    }
  }
  useEffect(() => {
    load();
    const off = window.steam?.onUpdate(setData);
    const offQuery = window.steam?.onQuery((state) => {
      setQuery(state);
      if (state.finished)
        message.info(
          `查询结束：已保存 ${state.completed} 个，跳过 ${state.skipped} 个。`,
        );
    });
    return () => {
      off?.();
      offQuery?.();
    };
  }, []);
  const accounts = data.Accounts;
  const filtered = useMemo(
    () =>
      accounts.filter(
        (a) =>
          (status === "全部账号" || a.Status === status) &&
          [a.Username, effective(a), a.Email, a.Note].some((v) =>
            v?.toLowerCase().includes(search.toLowerCase().trim()),
          ),
      ),
    [accounts, status, search],
  );
  useEffect(() => {
    setPage(1);
  }, [search, status]);
  useEffect(() => {
    setSelected((ids) => ids.filter((id) => accounts.some((a) => a.Id === id)));
  }, [accounts]);
  const current = accounts.find((a) => a.Id === data.CurrentAccountId);
  useLayoutEffect(() => {
    const region = tableRegion.current;
    if (!region) return;
    const header = region.querySelector('.ant-table-header');
    const pagination = region.querySelector('.ant-pagination');
    function measure() {
      const style = pagination && getComputedStyle(pagination);
      const paginationHeight = pagination ? pagination.offsetHeight + parseFloat(style.marginTop) + parseFloat(style.marginBottom) : 64;
      setTableHeight(Math.max(60, Math.floor(region.clientHeight - (header?.offsetHeight || 58) - paginationHeight - 2)));
    }
    const observer = new ResizeObserver(measure);
    observer.observe(region);
    if (header) observer.observe(header);
    if (pagination) observer.observe(pagination);
    measure();
    return () => observer.disconnect();
  }, [pageSize, filtered.length]);
  const allFiltered =
    filtered.length > 0 && filtered.every((a) => selected.includes(a.Id));
  const activeQuery = query && !query.finished;
  function selectAll(checked) {
    setSelected(
      checked
        ? [...new Set([...selected, ...filtered.map((a) => a.Id)])]
        : selected.filter((id) => !filtered.some((a) => a.Id === id)),
    );
  }
  function openEdit(account = {}) {
    setEdit(account);
    form.resetFields();
    form.setFieldsValue(account);
  }
  async function saveAccount() {
    const values = await form.validateFields();
    await run(async () => {
      const r = await api("save-account", {
        Account: { ...values, Id: edit.Id },
        Password: values.Password,
        EmailPassword: values.EmailPassword,
      });
      setData(r.data);
      setEdit(null);
      message.success("账号已保存");
    });
  }
  function login(account) {
    modal.confirm({
      title: `切换到 ${account.Username}？`,
      content:
        "切换会退出当前 Steam 和正在运行的 PUBG，请先保存游戏进度。登录后可能仍需完成 Steam 验证。",
      okText: "退出并切换",
      cancelText: "取消",
      onOk: () =>
        run(async () => {
          const r = await api("login", { Ids: [account.Id] });
          setData(r.data);
          message.success("Steam 已启动，必要时请完成登录验证");
        }),
    });
  }
  async function startQuery(ids) {
    await run(async () => {
      await api("query", { Ids: ids });
    });
  }
  async function remove(ids) {
    await run(async () => {
      const r = await api("delete", { Ids: ids });
      setData(r.data);
      setSelected((prev) => prev.filter((id) => !ids.includes(id)));
      message.success("账号已删除");
    });
  }
  const columns = [
    {
      title: "Steam 账号",
      dataIndex: "Username",
      width: 270,
      sorter: (a, b) => a.Username.localeCompare(b.Username),
      render: (_, a) => (
        <div className="account-cell">
          <div className={`avatar av-${a.Username.length % 4}`}>
            {a.Username.slice(0, 2).toUpperCase()}
          </div>
          <div className="account-identity">
            <Text title={a.Username} copyable={{ text: a.Username }}>
              <strong className="account-name">{a.Username}</strong>
            </Text>
            {a.Id === data.CurrentAccountId && (
              <span className="current-account-badge" title="当前账号"><CheckCircleFilled /> 当前</span>
            )}
          </div>
        </div>
      ),
    },
    {
      title: "游戏昵称 / 查询标识",
      width: 170,
      render: (_, a) => (
        <Text className="query-identifier" title={effective(a)} copyable={{ text: effective(a) }}><span className="query-name">{effective(a)}</span></Text>
      ),
    },
    {
      title: "生存等级",
      dataIndex: "Level",
      width: 110,
      sorter: (a, b) => (a.Level ?? -1) - (b.Level ?? -1),
      render: (level, a) =>
        level == null ? (
          <span className="muted">—</span>
        ) : (
          <div className="level-cell">
            {a.Tier > 0 && (
              <img
                src={
                  new URL(`../Assets/tier${a.Tier}.png`, import.meta.url).href
                }
                alt={`阶位 ${a.Tier}`}
              />
            )}
            <span>{level} 级</span>
          </div>
        ),
    },
    {
      title: "账号状态",
      dataIndex: "Status",
      width: 100,
      render: (value) => (
        <Tag
          bordered={false}
          color={
            value === "正常"
              ? "success"
              : value === "永久封禁"
                ? "error"
                : value === "临时封禁"
                  ? "warning"
                  : "default"
          }
        >
          {value === "正常" && <CheckCircleFilled />} {value || "未查询"}
        </Tag>
      ),
    },
    {
      title: "备注",
      dataIndex: "Note",
      width: 140,
      className: "account-note-cell",
      render: (note) => note?.trim() ? (
        <span className="account-note" title={note}>{note}</span>
      ) : null,
    },
    {
      title: "最近查询",
      dataIndex: "LastQueryAt",
      width: 110,
      sorter: (a, b) => a.LastQueryAt - b.LastQueryAt,
      render: (value) => <span className="time">{stamp(value)}</span>,
    },
    {
      title: "操作",
      key: "actions",
      fixed: "right",
      width: 140,
      render: (_, a) => (
        <Space size={2}>
          <Button
            type="link"
            size="small"
            disabled={busy || !!activeQuery}
            onClick={() => login(a)}
          >
            登录
          </Button>
          <Button
            type="link"
            size="small"
            disabled={busy || !!activeQuery}
            onClick={() => startQuery([a.Id])}
          >
            查询
          </Button>
          <Dropdown
            menu={{
              items: [
                { key: "edit", label: "编辑账号", icon: <EditOutlined /> },
                {
                  key: "delete",
                  label: "删除账号",
                  icon: <DeleteOutlined />,
                  danger: true,
                },
              ],
              onClick: ({ key }) =>
                key === "edit"
                  ? openEdit(a)
                  : modal.confirm({
                      title: "删除这个账号？",
                      content: "仅删除工具中的记录，不会删除 Steam 账号。",
                      okText: "删除",
                      okButtonProps: { danger: true },
                      cancelText: "取消",
                      onOk: () => remove([a.Id]),
                    }),
            }}
            disabled={busy || !!activeQuery}
          >
            <Button type="text" size="small" aria-label="更多账号操作">
              •••
            </Button>
          </Dropdown>
        </Space>
      ),
    },
  ];
  return (
    <div className="shell">
      <aside className="sidebar">
        <div className="brand">
          <div className="brand-mark">
            <img src={appLogo} alt="Steam Login Lite" />
          </div>
          <div>
            STEAM<span>LOGIN LITE</span>
          </div>
        </div>
        <div className="workspace-label">工作空间</div>
        <button className="nav-item active">
          <AppstoreOutlined />
          账号管理<span>{accounts.length}</span>
        </button>
        <button
          className="nav-item"
          onClick={() => {
            settingsForm.setFieldsValue(data.Settings);
            setSettings(true);
          }}
        >
          <SettingOutlined />
          偏好设置
        </button>
        <div className="side-bottom">
          <div className="local-badge">
            <span />
            本地账号管理
          </div>
          <p>
            密码加密存储于此电脑
            <br />
            关闭窗口即退出工具
          </p>
          <div className="version">本地版 / 版本 {env.version || "2.1.4"}</div>
        </div>
      </aside>
      <main>
        <header className="topbar">
          <span>
            工作空间 <span className="slash">/</span> <b>账号管理</b>
          </span>
          <div>
            <span className="privacy-dot" />
            本地存储 <span className="top-divider">|</span> STEAM LOGIN LITE
          </div>
        </header>
        <div className="content">
          <section className="page-heading">
            <div className="summary-stats" aria-label="账号概览">
              <span className="stat">全部 <b>{accounts.length}</b></span>
              <span className="stat summary-healthy">正常 <b>{accounts.filter((a) => a.Status === "正常").length}</b></span>
              <span className="stat summary-pending">待查询 <b>{accounts.filter((a) => !a.LastQueryAt).length}</b></span>
              <span className="stat current-summary" title={current?.Username || "尚未切换"}>
                <DesktopOutlined /> 当前 <b>{current?.Username || "尚未切换"}</b>
              </span>
            </div>
            <Space>
              <Button
                icon={<ImportOutlined />}
                disabled={busy || !!activeQuery || !!fatal}
                onClick={() => setImporting(true)}
              >
                批量导入
              </Button>
              <Button
                type="primary"
                icon={<PlusOutlined />}
                disabled={busy || !!activeQuery || !!fatal}
                onClick={() => openEdit()}
              >
                添加账号
              </Button>
            </Space>
          </section>
          {env.preview && (
            <Alert
              className="notice"
              type="info"
              showIcon
              message="Mac 界面预览 · 正式账号存储、查询同步和 Steam 切换请使用 Windows 绿色包。"
              action={
                <Button
                  size="small"
                  onClick={() =>
                    run(async () => {
                      const r = await api("demo");
                      setData(r.data);
                      setSelected([]);
                    })
                  }
                >
                  重置示例数据
                </Button>
              }
            />
          )}
          {fatal && (
            <Alert
              className="notice"
              type="error"
              showIcon
              message="账号数据加载失败"
              description={fatal}
              action={<Button onClick={load}>重试</Button>}
            />
          )}
          {activeQuery && (
            <div className="query-banner">
              <CloudSyncOutlined spin />
              <div>
                <strong>正在查询 {query.gameId}</strong>
                <p>{query.message}</p>
              </div>
              <Progress
                percent={Math.round(
                  ((query.completed + query.skipped) / query.total) * 100,
                )}
                size="small"
                style={{ width: 120 }}
              />
              <Button onClick={() => run(() => api("show-query"))}>
                打开查询窗口
              </Button>
            </div>
          )}
          <section className="table-card">
            <div className="table-toolbar">
              <Segmented
                options={["全部账号", "正常", "未查询", "临时封禁", "永久封禁"]}
                value={status}
                onChange={setStatus}
              />
              <Space>
                <Input
                  className="search"
                  allowClear
                  prefix={<SearchOutlined />}
                  placeholder="搜索账号、昵称或备注"
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                />
                <Tooltip title="刷新账号数据">
                  <Button
                    aria-label="刷新账号"
                    icon={<ReloadOutlined />}
                    onClick={load}
                    disabled={busy || !!activeQuery}
                  />
                </Tooltip>
              </Space>
            </div>
            <div className="selection-bar">
              <Checkbox
                checked={allFiltered}
                indeterminate={
                  !allFiltered && filtered.some((a) => selected.includes(a.Id))
                }
                onChange={(e) => selectAll(e.target.checked)}
              >
                全选筛选结果（{filtered.length}）
              </Checkbox>
              <span className="selection-count">
                已选择 <b>{selected.length}</b> 个
              </span>
              {selected.length > 0 && (
                <>
                  <Button
                    size="small"
                    type="link"
                    onClick={() => setSelected([])}
                  >
                    取消选择
                  </Button>
                  <span className="selection-spacer" />
                  <Button
                    size="small"
                    icon={<CloudSyncOutlined />}
                    disabled={busy || !!activeQuery}
                    onClick={() => startQuery(selected)}
                  >
                    查询所选
                  </Button>
                  <Popconfirm
                    title={`删除选中的 ${selected.length} 个账号？`}
                    description="删除后无法在界面中撤销。"
                    onConfirm={() => remove(selected)}
                    okText="删除"
                    cancelText="取消"
                  >
                    <Button
                      size="small"
                      danger
                      icon={<DeleteOutlined />}
                      disabled={busy || !!activeQuery}
                    >
                      删除
                    </Button>
                  </Popconfirm>
                </>
              )}
            </div>
            <div className="table-region" ref={tableRegion}>
            <Table
              columns={columns}
              dataSource={filtered}
              rowKey="Id"
              rowClassName={(account) => account.Id === data.CurrentAccountId ? "current-account-row" : ""}
              loading={loading || busy}
              rowSelection={{
                selectedRowKeys: selected,
                onChange: setSelected,
                preserveSelectedRowKeys: true,
                selections: [
                  Table.SELECTION_ALL,
                  Table.SELECTION_INVERT,
                  Table.SELECTION_NONE,
                ],
              }}
              scroll={{ x: 1072, y: tableHeight }}
              pagination={{
                current: page,
                onChange: (nextPage, nextSize) => {
                  setPage(nextSize !== pageSize ? 1 : nextPage);
                  setPageSize(nextSize);
                },
                pageSize,
                pageSizeOptions: [10, 20, 50, 100],
                showSizeChanger: true,
                showQuickJumper: true,
                showTotal: (total) => `共 ${total} 个账号`,
                hideOnSinglePage: false,
              }}
              locale={{
                emptyText: (
                  <Empty
                    image={Empty.PRESENTED_IMAGE_SIMPLE}
                    description={
                      search || status !== "全部账号"
                        ? "没有匹配的账号"
                        : "还没有账号，添加一个开始使用"
                    }
                  >
                    <Button
                      type="primary"
                      icon={<PlusOutlined />}
                      disabled={!!fatal}
                      onClick={() => openEdit()}
                    >
                      添加账号
                    </Button>
                  </Empty>
                ),
              }}
            />
            </div>
          </section>
          <footer>
            <span>
              <CheckCircleFilled /> 账号数据保存在当前 Windows
              用户目录，更新工具不会丢失。
            </span>
            <span>PUBG.PLUS 提供查询数据</span>
          </footer>
        </div>
      </main>
      <Drawer
        title={edit?.Id ? "编辑账号" : "添加账号"}
        open={edit !== null}
        width={440}
        onClose={() => setEdit(null)}
        extra={
          <Button type="primary" loading={busy} onClick={saveAccount}>
            保存账号
          </Button>
        }
        destroyOnHidden
      >
        <p className="drawer-description">
          查询标识 留空时，将使用 Steam 账号名。
        </p>
        <Form form={form} layout="vertical">
          <Form.Item
            name="Username"
            label="Steam 账号"
            rules={[
              {
                required: true,
                whitespace: true,
                message: "请输入 Steam 账号",
              },
            ]}
          >
            <Input autoComplete="off" />
          </Form.Item>
          <Form.Item
            name="Password"
            label="Steam 密码"
            extra={
              edit?.Id
                ? "留空保留原密码；密码不会回显。"
                : "密码使用当前 Windows 用户加密。"
            }
            rules={[{ required: !edit?.Id, message: "请输入密码" }]}
          >
            <Input.Password autoComplete="new-password" />
          </Form.Item>
          <Form.Item name="GameId" label="游戏昵称 / 查询标识">
            <Input placeholder="与游戏中的昵称一致" />
          </Form.Item>
          <Form.Item
            name="Email"
            label="邮箱"
            rules={[{ type: "email", message: "请输入正确的邮箱地址" }]}
          >
            <Input />
          </Form.Item>
          <Form.Item
            name="EmailPassword"
            label="邮箱密码"
            extra="留空保留原邮箱密码。"
          >
            <Input.Password autoComplete="new-password" />
          </Form.Item>
          <Form.Item name="Note" label="备注">
            <Input.TextArea rows={3} maxLength={500} showCount />
          </Form.Item>
        </Form>
      </Drawer>
      <Modal
        title="批量导入账号"
        open={importing}
        onCancel={() => {
          setImporting(false);
          setImportText("");
        }}
        okText="导入账号"
        cancelText="取消"
        confirmLoading={busy}
        width={650}
        onOk={() =>
          run(async () => {
            if (!importText.trim()) throw new Error("请粘贴账号数据");
            const r = await api("import", { Text: importText });
            setData(r.data);
            setImporting(false);
            setImportText("");
            message.success(
              `已导入 ${r.extra.added} 个，跳过重复账号 ${r.extra.skipped} 个`,
            );
          })
        }
      >
        <Alert
          type="info"
          showIcon
          message="每行一个账号；支持 2 段、4 段、5 段和原中文标签格式。重复账号会跳过，不覆盖原数据。"
        />
        <pre className="import-example">
          账号----密码{"\n"}账号----密码----邮箱----邮箱密码----游戏昵称
        </pre>
        <Input.TextArea
          rows={10}
          value={importText}
          onChange={(e) => setImportText(e.target.value)}
          placeholder="将账号数据粘贴到这里…"
        />
        <p className="muted">
          所有行校验通过后才会保存；导入完成后输入内容会清空。
        </p>
      </Modal>
      <Drawer
        title="偏好设置"
        width={460}
        open={settings}
        onClose={() => setSettings(false)}
        extra={
          <Button
            type="primary"
            loading={busy}
            onClick={() =>
              run(async () => {
                const values = await settingsForm.validateFields();
                const r = await api("settings", { Settings: values });
                setData(r.data);
                setSettings(false);
                message.success("设置已保存");
              })
            }
          >
            保存设置
          </Button>
        }
      >
        <section className="motion-settings">
          <h3>界面动效</h3>
          <p className="drawer-description">立即生效，自动保存在此电脑。</p>
          <Segmented
            block
            aria-label="界面动效"
            value={motionMode}
            onChange={setMotionMode}
            options={[
              { label: "流畅动效", value: "full" },
              { label: "跟随系统", value: "system" },
              { label: "减少动效", value: "reduced" },
            ]}
          />
        </section>
        <Form layout="vertical" form={settingsForm}>
          <h3>Steam 启动</h3>
          <Form.Item
            name="SteamPath"
            label="Steam 主程序路径"
            rules={[{ required: true, message: "请选择 Steam 主程序" }]}
          >
            <Input placeholder="C:\Program Files (x86)\Steam\steam.exe" />
          </Form.Item>
          <Button
            onClick={() =>
              run(async () => {
                const file = await api("choose-steam");
                if (file) settingsForm.setFieldValue("SteamPath", file);
              })
            }
          >
            浏览文件
          </Button>
          <Form.Item
            name="StartupDelaySeconds"
            label="登录后处理延迟（秒）"
            style={{ marginTop: 24 }}
          >
            <InputNumber min={2} max={120} />
          </Form.Item>
          <Form.Item
            name="OpenLibrary"
            label="登录后打开游戏库"
            valuePropName="checked"
          >
            <Switch />
          </Form.Item>
          <Form.Item
            name="CloseRecommendations"
            label="关闭推荐 / 新闻窗口"
            valuePropName="checked"
          >
            <Switch />
          </Form.Item>
          <Form.Item
            name="CloseFriendsList"
            label="关闭好友列表"
            valuePropName="checked"
          >
            <Switch />
          </Form.Item>
          <h3>查询</h3>
          <Form.Item
            name="QueryIntervalMilliseconds"
            label="批量查询间隔（毫秒）"
          >
            <InputNumber min={500} max={30000} step={500} />
          </Form.Item>
          <Alert
            type="info"
            showIcon
            message="查询使用工具自带浏览器，无需安装 WebView2。关闭工具会停止尚未完成的查询及登录后窗口处理。"
          />
        </Form>
      </Drawer>
    </div>
  );
}
function Application() {
  const [motionMode, updateMotionMode] = useState(() => {
    try {
      const saved = localStorage.getItem("steam-login-motion");
      return ["full", "system", "reduced"].includes(saved) ? saved : "full";
    } catch {
      return "full";
    }
  });
  const [systemReduced, setSystemReduced] = useState(
    () => window.matchMedia("(prefers-reduced-motion: reduce)").matches,
  );
  useEffect(() => {
    const media = window.matchMedia("(prefers-reduced-motion: reduce)");
    const changed = () => setSystemReduced(media.matches);
    media.addEventListener("change", changed);
    changed();
    return () => media.removeEventListener("change", changed);
  }, []);
  const reduced = motionMode === "reduced" || (motionMode === "system" && systemReduced);
  // One policy for custom styles and Ant Design, independent of the OS default.
  document.documentElement.dataset.motion = reduced ? "reduced" : "full";
  function setMotionMode(value) {
    updateMotionMode(value);
    try { localStorage.setItem("steam-login-motion", value); } catch { /* Session preference still applies. */ }
  }
  return (
  <ConfigProvider
    locale={zhCN}
    theme={{
      token: {
        // Keep the provider tree stable when changing motion preferences.
        motion: true,
        motionDurationFast: reduced ? "0s" : "0.15s",
        motionDurationMid: reduced ? "0s" : "0.24s",
        motionDurationSlow: reduced ? "0s" : "0.32s",
        colorPrimary: "#3867ed",
        colorText: "#25324b",
        colorTextSecondary: "#8792a5",
        colorBgLayout: "#f4f6fa",
        borderRadius: 8,
        fontFamily:
          '-apple-system, BlinkMacSystemFont, "Segoe UI", "Microsoft YaHei", sans-serif',
        controlHeight: 36,
      },
      components: {
        Table: {
          headerBg: "#f9fafc",
          headerColor: "#7b879b",
          rowHoverBg: "#f5f8ff",
          cellPaddingBlock: 4,
        },
        Button: { primaryShadow: "0 4px 10px rgba(56,103,237,.16)" },
        Segmented: { trackBg: "#f2f4f8", itemSelectedBg: "#fff" },
      },
    }}
  >
    <MotionProvider motion={!reduced}>
      <AntApp>
        <Manager motionMode={motionMode} setMotionMode={setMotionMode} />
      </AntApp>
    </MotionProvider>
  </ConfigProvider>,
  );
}
createRoot(document.getElementById("root")).render(<Application />);
