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

#define APP_DIR "QizhouPavilion"

static GtkWidget *main_window = NULL;
static GtkWidget *web_view = NULL;

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

static gboolean on_key_press(GtkWidget *widget, GdkEventKey *event, gpointer data)
{
    (void)widget;
    (void)data;
    if (web_view == NULL)
        return FALSE;

    gboolean ctrl = (event->state & GDK_CONTROL_MASK) != 0;
    if (event->keyval == GDK_KEY_F5 ||
        (ctrl && (event->keyval == GDK_KEY_r || event->keyval == GDK_KEY_R))) {
        webkit_web_view_reload(WEBKIT_WEB_VIEW(web_view));
        return TRUE;
    }
    if (ctrl && (event->keyval == GDK_KEY_l || event->keyval == GDK_KEY_L)) {
        char *url = prompt_url();
        if (url != NULL) {
            save_url(url);
            webkit_web_view_load_uri(WEBKIT_WEB_VIEW(web_view), url);
            g_free(url);
        }
        return TRUE;
    }
    return FALSE;
}

int main(int argc, char **argv)
{
    gtk_init(&argc, &argv);

    main_window = gtk_window_new(GTK_WINDOW_TOPLEVEL);
    gtk_window_set_title(GTK_WINDOW(main_window), "琪舟阁");
    gtk_window_set_default_size(GTK_WINDOW(main_window), 1100, 760);
    gtk_window_set_position(GTK_WINDOW(main_window), GTK_WIN_POS_CENTER);
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
    g_signal_connect(main_window, "key-press-event", G_CALLBACK(on_key_press), NULL);

    webkit_web_view_load_uri(WEBKIT_WEB_VIEW(web_view), url);
    g_free(url);

    gtk_widget_show_all(main_window);
    gtk_window_present(GTK_WINDOW(main_window));
    gtk_main();
    return 0;
}
