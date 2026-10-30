/*
 * 琪舟阁 桌面版（Linux）：GTK3 + 系统 WebKitGTK，不捆绑浏览器内核，
 * 二进制几十 KB，内存由系统 WebKit 进程按需占用。
 *
 * 构建： gcc -O2 -o qizhoupavilion qizhoupavilion.c \
 *          $(pkg-config --cflags --libs gtk+-3.0 webkit2gtk-4.1)
 */
#include <gtk/gtk.h>
#include <webkit2/webkit2.h>
#include <stdlib.h>
#include <string.h>

#include "menu_js.h"
#include "inject_js.h"
#include "error_html.h"

#define APP_DIR "QizhouPavilion"
/* 打不开时那张提示页的 base uri，用来认「现在亮的是一张提示页」 */
#define ERROR_BASE "https://qizhouge.app/"

static GtkWidget *main_window = NULL;
static GtkWidget *web_view = NULL;
static GtkWidget *fs_item = NULL;
static char *failed_url = NULL;

static void act_home(void);

static char *config_path(void)
{
    const char *xdg = getenv("XDG_CONFIG_HOME");
    char *dir;
    if (xdg != NULL && *xdg != '\0') {
        dir = g_build_filename(xdg, APP_DIR, NULL);
    } else {
        const char *home = getenv("HOME");
        if (home == NULL || *home == '\0')
            home = ".";
        dir = g_build_filename(home, ".config", APP_DIR, NULL);
    }
    g_mkdir_with_parents(dir, 0755);
    char *path = g_build_filename(dir, "home.txt", NULL);
    g_free(dir);
    return path;
}

static char *load_url(void)
{
    char *path = config_path();
    char *text = NULL;
    char *result = NULL;
    if (g_file_get_contents(path, &text, NULL, NULL)) {
        g_strstrip(text);
        if (*text != '\0')
            result = g_strdup(text);
        g_free(text);
    }
    g_free(path);
    return result;
}

static void save_url(const char *url)
{
    char *path = config_path();
    g_file_set_contents(path, url, -1, NULL);
    g_free(path);
}

static char *normalize_url(const char *raw)
{
    if (raw == NULL)
        return NULL;
    char *s = g_strdup(raw);
    g_strstrip(s);
    if (*s == '\0') {
        g_free(s);
        return NULL;
    }

    char *full;
    if (g_str_has_prefix(s, "http://") || g_str_has_prefix(s, "https://"))
        full = g_strdup(s);
    else
        full = g_strdup_printf("https://%s", s);
    g_free(s);

    size_t n = strlen(full);
    while (n > 8 && full[n - 1] == '/')
        full[--n] = '\0';

    if (n <= 8 || strchr(full, '.') == NULL) {
        g_free(full);
        return NULL;
    }
    return full;
}

static char *prompt_url(void)
{
    GtkWidget *dialog = gtk_dialog_new_with_buttons(
        "琪舟阁", GTK_WINDOW(main_window), GTK_DIALOG_MODAL,
        "取消", GTK_RESPONSE_CANCEL,
        "进入", GTK_RESPONSE_ACCEPT, NULL);
    gtk_window_set_default_size(GTK_WINDOW(dialog), 420, -1);
    gtk_window_set_position(GTK_WINDOW(dialog), GTK_WIN_POS_CENTER_ON_PARENT);

    GtkWidget *area = gtk_dialog_get_content_area(GTK_DIALOG(dialog));
    GtkWidget *box = gtk_box_new(GTK_ORIENTATION_VERTICAL, 8);
    gtk_container_set_border_width(GTK_CONTAINER(box), 16);

    GtkWidget *label = gtk_label_new("输入你部署的导航站域名");
    gtk_widget_set_halign(label, GTK_ALIGN_START);
    GtkWidget *entry = gtk_entry_new();
    gtk_entry_set_placeholder_text(GTK_ENTRY(entry), "例如 nav.example.com");
    gtk_entry_set_activates_default(GTK_ENTRY(entry), TRUE);

    gtk_box_pack_start(GTK_BOX(box), label, FALSE, FALSE, 0);
    gtk_box_pack_start(GTK_BOX(box), entry, FALSE, FALSE, 0);
    gtk_container_add(GTK_CONTAINER(area), box);

    gtk_dialog_set_default_response(GTK_DIALOG(dialog), GTK_RESPONSE_ACCEPT);
    gtk_widget_show_all(dialog);
    gtk_widget_grab_focus(entry);

    char *result = NULL;
    if (gtk_dialog_run(GTK_DIALOG(dialog)) == GTK_RESPONSE_ACCEPT)
        result = normalize_url(gtk_entry_get_text(GTK_ENTRY(entry)));

    gtk_widget_destroy(dialog);
    return result;
}

/* ---------- 通用动作：菜单和网页回传都走这几个 ---------- */

static gboolean is_fullscreen = FALSE;

static void act_fullscreen(void)
{
    if (main_window == NULL)
        return;
    if (is_fullscreen)
        gtk_window_unfullscreen(GTK_WINDOW(main_window));
    else
        gtk_window_fullscreen(GTK_WINDOW(main_window));
    is_fullscreen = !is_fullscreen;

    if (fs_item != NULL)
        gtk_menu_item_set_label(GTK_MENU_ITEM(fs_item), is_fullscreen ? "退出全屏" : "全屏");

    if (web_view != NULL) {
        char *js = g_strdup_printf("window.__zqFullscreen&&window.__zqFullscreen(%s);",
                                   is_fullscreen ? "true" : "false");
        webkit_web_view_run_javascript(WEBKIT_WEB_VIEW(web_view), js, NULL, NULL, NULL);
        g_free(js);
    }
}

/* 当前显示的是不是那张「打不开」提示页 */
static gboolean on_error_page(void)
{
    if (web_view == NULL)
        return FALSE;
    const char *uri = webkit_web_view_get_uri(WEBKIT_WEB_VIEW(web_view));
    return uri != NULL && g_str_has_prefix(uri, ERROR_BASE);
}

static void act_retry(void)
{
    if (web_view == NULL)
        return;
    if (failed_url != NULL)
        webkit_web_view_load_uri(WEBKIT_WEB_VIEW(web_view), failed_url);
    else
        act_home();
}

static void act_reload(void)
{
    if (web_view == NULL)
        return;
    /* 提示页上「刷新」等于重试刚才那个地址，不是重载这张提示页 */
    if (on_error_page()) {
        act_retry();
        return;
    }
    webkit_web_view_reload(WEBKIT_WEB_VIEW(web_view));
}

static void act_back(void)
{
    if (web_view == NULL)
        return;
    WebKitWebView *wv = WEBKIT_WEB_VIEW(web_view);
    if (webkit_web_view_can_go_back(wv))
        webkit_web_view_go_back(wv);
}

static void act_forward(void)
{
    if (web_view == NULL)
        return;
    WebKitWebView *wv = WEBKIT_WEB_VIEW(web_view);
    if (webkit_web_view_can_go_forward(wv))
        webkit_web_view_go_forward(wv);
}

static void act_home(void)
{
    if (web_view == NULL)
        return;
    char *home = load_url();
    if (home != NULL) {
        webkit_web_view_load_uri(WEBKIT_WEB_VIEW(web_view), home);
        g_free(home);
    }
}

static void act_change(void)
{
    if (web_view == NULL)
        return;
    char *url = prompt_url();
    if (url != NULL) {
        save_url(url);
        webkit_web_view_load_uri(WEBKIT_WEB_VIEW(web_view), url);
        g_free(url);
    }
}

static void act_dispatch(const char *action)
{
    if (action == NULL)
        return;
    if (g_strcmp0(action, "reload") == 0)
        act_reload();
    else if (g_strcmp0(action, "retry") == 0)
        act_retry();
    else if (g_strcmp0(action, "fullscreen") == 0)
        act_fullscreen();
    else if (g_strcmp0(action, "change") == 0)
        act_change();
    else if (g_strcmp0(action, "back") == 0)
        act_back();
    else if (g_strcmp0(action, "forward") == 0)
        act_forward();
    else if (g_strcmp0(action, "home") == 0)
        act_home();
}

/* ---------- 自绘右键菜单 ---------- */

static void on_load_changed(WebKitWebView *view, WebKitLoadEvent load_event, gpointer user_data)
{
    (void)user_data;
    if (load_event == WEBKIT_LOAD_FINISHED) {
        webkit_web_view_run_javascript(view, MENU_JS, NULL, NULL, NULL);
        webkit_web_view_run_javascript(view, INJECT_JS, NULL, NULL, NULL);
    }
}

/*
 * 打不开时换成 App 自己的提示页，不留 WebKit 那片空白。
 * 菜单回传的 zqnavgo: 会让当前这次加载被取消，那种取消要放过去。
 */
static gboolean on_load_failed(WebKitWebView *view, WebKitLoadEvent load_event,
                               const char *failing_uri, GError *error, gpointer user_data)
{
    (void)load_event;
    (void)user_data;
    if (error != NULL && error->code == WEBKIT_NETWORK_ERROR_CANCELLED)
        return FALSE;
    if (failing_uri != NULL && g_str_has_prefix(failing_uri, "zqnavgo:"))
        return FALSE;
    if (failing_uri != NULL) {
        g_free(failed_url);
        failed_url = g_strdup(failing_uri);
    }
    webkit_web_view_load_html(view, ERROR_HTML, ERROR_BASE);
    return TRUE;
}

/* 网页侧长按菜单走 zqnavgo:<动作名>，这里拦下来交给统一的动作分发 */
static gboolean on_decide_policy(WebKitWebView *view, WebKitPolicyDecision *decision,
                                 WebKitPolicyDecisionType type, gpointer user_data)
{
    (void)view;
    (void)user_data;
    if (type != WEBKIT_POLICY_DECISION_TYPE_NAVIGATION_ACTION)
        return FALSE;

    WebKitNavigationAction *action =
        webkit_navigation_policy_decision_get_navigation_action(
            WEBKIT_NAVIGATION_POLICY_DECISION(decision));
    if (action == NULL)
        return FALSE;

    WebKitURIRequest *request = webkit_navigation_action_get_request(action);
    const char *uri = request != NULL ? webkit_uri_request_get_uri(request) : NULL;
    if (uri == NULL || !g_str_has_prefix(uri, "zqnavgo:"))
        return FALSE;

    webkit_policy_decision_ignore(decision);

    char *decoded = g_uri_unescape_string(uri + strlen("zqnavgo:"), NULL);
    if (decoded != NULL) {
        act_dispatch(decoded);
        g_free(decoded);
    }
    return TRUE;
}

static gboolean on_context_menu(WebKitWebView *view, WebKitContextMenu *menu,
                                GdkEvent *event, WebKitHitTestResult *hit_test,
                                gpointer user_data)
{
    (void)view;
    (void)menu;
    (void)hit_test;
    (void)user_data;
    double x = 0;
    double y = 0;
    if (event != NULL && gdk_event_get_coords(event, &x, &y)) {
        char *script = g_strdup_printf(
            "window.__zqMenu&&window.__zqMenu.show(%d,%d);", (int)x, (int)y);
        webkit_web_view_run_javascript(WEBKIT_WEB_VIEW(web_view), script, NULL, NULL, NULL);
        g_free(script);
    }
    return TRUE; /* 吃掉 WebKit 自带的英文菜单 */
}

/* ---------- 标题栏里的菜单 ---------- */

static void on_menu_action(GtkMenuItem *item, gpointer data)
{
    (void)item;
    act_dispatch((const char *)data);
}

static void on_menu_quit(GtkMenuItem *item, gpointer data)
{
    (void)item;
    (void)data;
    gtk_main_quit();
}

static GtkWidget *make_header_bar(void)
{
    GtkWidget *header = gtk_header_bar_new();
    gtk_header_bar_set_title(GTK_HEADER_BAR(header), "琪舟阁");
    gtk_header_bar_set_show_close_button(GTK_HEADER_BAR(header), TRUE);

    GtkWidget *menu_btn = gtk_menu_button_new();
    gtk_widget_set_tooltip_text(menu_btn, "菜单");
    gtk_button_set_relief(GTK_BUTTON(menu_btn), GTK_RELIEF_NONE);
    gtk_container_add(GTK_CONTAINER(menu_btn), gtk_label_new("☰"));

    GtkWidget *menu = gtk_menu_new();
    /* 与 Windows 右键菜单、手机长按菜单完全一致的六项，不挂快捷键 */
    static const char *const items[6][2] = {
        { "reload", "刷新" },
        { "fullscreen", "全屏" },
        { "change", "更换域名" },
        { "back", "后退上一页" },
        { "forward", "前进下一页" },
        { "home", "返回主页面" }
    };
    int i;
    for (i = 0; i < 6; i++) {
        GtkWidget *it = gtk_menu_item_new_with_label(items[i][1]);
        if (g_strcmp0(items[i][0], "fullscreen") == 0)
            fs_item = it;
        gtk_menu_shell_append(GTK_MENU_SHELL(menu), it);
        g_signal_connect(it, "activate", G_CALLBACK(on_menu_action),
                         (gpointer)items[i][0]);
    }

    GtkWidget *i_quit = gtk_menu_item_new_with_label("退出琪舟阁");
    gtk_menu_shell_append(GTK_MENU_SHELL(menu), gtk_separator_menu_item_new());
    gtk_menu_shell_append(GTK_MENU_SHELL(menu), i_quit);
    g_signal_connect(i_quit, "activate", G_CALLBACK(on_menu_quit), NULL);

    gtk_widget_show_all(menu);
    gtk_menu_button_set_popup(GTK_MENU_BUTTON(menu_btn), menu);
    gtk_header_bar_pack_end(GTK_HEADER_BAR(header), menu_btn);

    return header;
}

int main(int argc, char **argv)
{
    gtk_init(&argc, &argv);

    main_window = gtk_window_new(GTK_WINDOW_TOPLEVEL);
    gtk_window_set_title(GTK_WINDOW(main_window), "琪舟阁");
    gtk_window_set_default_size(GTK_WINDOW(main_window), 1100, 760);
    gtk_window_set_position(GTK_WINDOW(main_window), GTK_WIN_POS_CENTER);
    gtk_window_set_titlebar(GTK_WINDOW(main_window), make_header_bar());
    g_signal_connect(main_window, "destroy", G_CALLBACK(gtk_main_quit), NULL);

    char *url = NULL;
    if (argc > 1)
        url = normalize_url(argv[1]);
    if (url == NULL)
        url = load_url();
    if (url == NULL) {
        url = prompt_url();
        if (url == NULL)
            return 0;
        save_url(url);
    }

    web_view = webkit_web_view_new();
    gtk_container_add(GTK_CONTAINER(main_window), web_view);
    g_signal_connect(web_view, "load-changed", G_CALLBACK(on_load_changed), NULL);
    g_signal_connect(web_view, "load-failed", G_CALLBACK(on_load_failed), NULL);
    g_signal_connect(web_view, "context-menu", G_CALLBACK(on_context_menu), NULL);
    g_signal_connect(web_view, "decide-policy", G_CALLBACK(on_decide_policy), NULL);

    webkit_web_view_load_uri(WEBKIT_WEB_VIEW(web_view), url);
    g_free(url);

    gtk_widget_show_all(main_window);
    gtk_window_present(GTK_WINDOW(main_window));
    gtk_main();
    return 0;
}
