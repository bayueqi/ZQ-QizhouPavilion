# 项目里没有反射、没有三方库：Activity 由清单引用、View 由布局引用，
# R8 自己判断得出来，不需要额外 -keep。开启混淆+资源压缩以缩小安装包。
-keepattributes SourceFile,LineNumberTable
-renamesourcefileattribute SourceFile
